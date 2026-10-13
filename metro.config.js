const { getDefaultConfig } = require('expo/metro-config');

const config = getDefaultConfig(__dirname);

// The .NET backend lives in this repo; keep Metro from crawling its sources and build output.
const existing = config.resolver.blockList;
config.resolver.blockList = [...(Array.isArray(existing) ? existing : existing ? [existing] : []), /[\/]backend[\/].*/];

module.exports = config;
