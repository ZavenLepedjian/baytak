// Build-time additions to app.json (Android):
// - Google Maps key: set GOOGLE_MAPS_API_KEY as an EAS environment variable. Without a key the app still builds and the
//   building-location screen falls back to the device's own location.
// - Firebase config for push notifications: put google-services.json in the project root (or set the EAS file variable
//   GOOGLE_SERVICES_JSON). Without it the build works but Android cannot get a push token, so no notifications arrive.
const fs = require('fs');
const path = require('path');

module.exports = ({ config }) => {
  const key = process.env.GOOGLE_MAPS_API_KEY;
  const extra = { ...(config.extra || {}), mapsEnabled: Boolean(key) };
  const googleServicesFile =
    process.env.GOOGLE_SERVICES_JSON || (fs.existsSync(path.join(__dirname, 'google-services.json')) ? './google-services.json' : undefined);

  const android = { ...config.android };
  if (googleServicesFile) android.googleServicesFile = googleServicesFile;
  if (key) android.config = { ...(config.android && config.android.config), googleMaps: { apiKey: key } };
  return { ...config, extra, android };
};
