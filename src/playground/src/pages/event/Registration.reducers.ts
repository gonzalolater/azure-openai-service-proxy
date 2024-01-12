import { RegistrationState } from "./Registration.state";

export type RegistrationAction = {
  type: "PROFILE_LOADED";
  payload: {
    loaded: RegistrationState["profileLoaded"];
    profile: RegistrationState["profile"];
  };
};

export const reducer = (
  state: RegistrationState,
  action: RegistrationAction
) => {
  switch (action.type) {
    case "PROFILE_LOADED":
      return {
        ...state,
        profileLoaded: action.payload.loaded,
        profile: action.payload.profile,
      };
    default:
      return state;
  }
};
