using System.Data;
using System.Data.Common;
using AzureOpenAIProxy.Management.Components.ModelManagement;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace AzureOpenAIProxy.Management.Services;

public class ModelService(IAuthService authService, AoaiProxyContext db, IConfiguration configuration) : IModelService
{

    private const string PostgresEncryptionKey = "PostgresEncryptionKey";
    private readonly DbConnection connection = db.Database.GetDbConnection();

    private async Task<byte[]?> PostgresEncryptValue(string value)
    {
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync();

        string? postgresEncryptionKey = configuration[PostgresEncryptionKey];

        using DbCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT aoai.pgp_sym_encrypt(@value, @postgresEncryptionKey);";
        command.Parameters.Add(new NpgsqlParameter("value", NpgsqlDbType.Text) { Value = value });
        command.Parameters.Add(new NpgsqlParameter("postgresEncryptionKey", NpgsqlDbType.Text) { Value = postgresEncryptionKey });

        using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return reader[0] as byte[];
    }

    private async Task<string?> PostgresDecryptValue(byte[] value)
    {
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync();

        string? postgresEncryptionKey = configuration[PostgresEncryptionKey];

        using DbCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT aoai.pgp_sym_decrypt(@value, @postgresEncryptionKey)";
        command.Parameters.Add(new NpgsqlParameter("value", NpgsqlDbType.Bytea) { Value = value });
        command.Parameters.Add(new NpgsqlParameter("postgresEncryptionKey", NpgsqlDbType.Text) { Value = postgresEncryptionKey });

        using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return reader[0] as string;
    }

    public async Task<OwnerCatalog> AddOwnerCatalogAsync(ModelEditorModel model)
    {
        Owner owner = await authService.GetCurrentOwnerAsync();

        byte[]? endpointKey = await PostgresEncryptValue(model.EndpointKey!);
        byte[]? endpointUrl = await PostgresEncryptValue(model.EndpointUrl!);

        OwnerCatalog catalog = new()
        {
            Owner = owner,
            Active = model.Active,
            FriendlyName = model.FriendlyName!,
            DeploymentName = model.DeploymentName!,
            Location = model.Location!,
            ModelType = model.ModelType!.Value,
            EndpointKeyEncrypted = endpointKey!,
            EndpointUrlEncrypted = endpointUrl!
        };

        await db.OwnerCatalogs.AddAsync(catalog);
        await db.SaveChangesAsync();

        return catalog;
    }

    public async Task DeleteOwnerCatalogAsync(Guid catalogId)
    {
        OwnerCatalog? ownerCatalog = await db.OwnerCatalogs.FindAsync(catalogId);

        if (ownerCatalog is null)
        {
            return;
        }

        // find if the resource is used in an event or has metrics
        var usageInfo = await db.OwnerCatalogs.Where(oc => oc.CatalogId == catalogId)
            .Select(oc => new
            {
                UsedInEvent = oc.Events.Count != 0
            })
            .FirstAsync();

        // block deletion when it's in use to avoid cascading deletes
        if (usageInfo.UsedInEvent)
        {
            return;
        }

        db.OwnerCatalogs.Remove(ownerCatalog);
        await db.SaveChangesAsync();
    }

    public async Task<OwnerCatalog> GetOwnerCatalogAsync(Guid catalogId)
    {
        var result = await db.OwnerCatalogs.FindAsync(catalogId);

        string? endpointKey = await PostgresDecryptValue(result!.EndpointKeyEncrypted);
        string? endpointUrl = await PostgresDecryptValue(result!.EndpointUrlEncrypted);

        result.EndpointKey = endpointKey!;
        result.EndpointUrl = endpointUrl!;

        return result;
    }

    public async Task<IEnumerable<OwnerCatalog>> GetOwnerCatalogsAsync()
    {
        string entraId = await authService.GetCurrentUserEntraIdAsync();
        var catalogItems = await db.OwnerCatalogs.Where(oc => oc.Owner.OwnerId == entraId).OrderBy(oc => oc.FriendlyName).ToListAsync();
        return catalogItems;
    }

    public async Task UpdateOwnerCatalogAsync(OwnerCatalog ownerCatalog)
    {
        OwnerCatalog? existingCatalog = await db.OwnerCatalogs.FindAsync(ownerCatalog.CatalogId);

        if (existingCatalog is null)
        {
            return;
        }

        byte[]? endpointKey = await PostgresEncryptValue(ownerCatalog.EndpointKey);
        byte[]? endpointUrl = await PostgresEncryptValue(ownerCatalog.EndpointUrl);

        existingCatalog.FriendlyName = ownerCatalog.FriendlyName;
        existingCatalog.DeploymentName = ownerCatalog.DeploymentName;
        existingCatalog.ModelType = ownerCatalog.ModelType;
        existingCatalog.Location = ownerCatalog.Location;
        existingCatalog.Active = ownerCatalog.Active;
        existingCatalog.EndpointKeyEncrypted = endpointKey!;
        existingCatalog.EndpointUrlEncrypted = endpointUrl!;

        db.OwnerCatalogs.Update(existingCatalog);
        await db.SaveChangesAsync();
    }
}
