// The Arabic texts for the iOS permission prompts (locales/ar.json) are copied into the Android build too, as
// values-b+ar/strings.xml, where they have no default-language twin. Lint's ExtraTranslation check treats that as fatal and
// fails the release build. The strings are harmless (Android never reads them), so only that check is switched off.
const { withAppBuildGradle } = require('expo/config-plugins');

const MARK = '// baytak: ExtraTranslation off';

function addLintRule(gradle) {
  if (gradle.includes(MARK)) return gradle;
  return gradle.replace(/android\s*\{/, (opening) => `${opening}\n    ${MARK} (see plugins/withAndroidLint.js)\n    lint {\n        disable 'ExtraTranslation'\n    }`);
}

module.exports = (config) => withAppBuildGradle(config, (c) => {
  c.modResults.contents = addLintRule(c.modResults.contents);
  return c;
});
module.exports.addLintRule = addLintRule;
