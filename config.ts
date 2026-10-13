/**
 * Central app configuration.
 *
 * Values come from build-time environment variables (EXPO_PUBLIC_*), see .env.example.
 * Anything bundled into a mobile app can be extracted, so the function key is not a secret:
 * every endpoint is protected by the user's JWT on the server.
 */
export const API_BASE_URL =
  process.env.EXPO_PUBLIC_API_BASE_URL || 'https://func-bms.azurewebsites.net/api';

/** Azure Functions host key, supplied at build time (EAS environment variable / local .env). Never hardcode it. */
export const API_KEY = process.env.EXPO_PUBLIC_API_KEY ?? '';

if (__DEV__ && !API_KEY) {
  console.warn('EXPO_PUBLIC_API_KEY is not set: authenticated API calls will be rejected by Azure. See .env.example.');
}

export const EAS_PROJECT_ID = '5ade25db-c3e0-4af9-b6bc-9adf24f1bce8';

/** Request timeout for every API call. */
export const REQUEST_TIMEOUT_MS = 20000;

/** Buildings farther than this (metres) from the device are hidden by default when joining. */
export const NEARBY_BUILDING_RADIUS_M = 10000;

export const FEATURES = {
  /** The wallet screen is a work-in-progress (local sample data only). */
  wallet: true,
};
