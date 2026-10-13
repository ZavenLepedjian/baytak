import React, { useEffect, useState } from 'react';
import { StatusBar } from 'expo-status-bar';
import { Asset } from 'expo-asset';
import { useFonts } from 'expo-font';
import * as SplashScreen from 'expo-splash-screen';
import { Ionicons } from '@expo/vector-icons';
// Import each weight on its own: the package index would bundle all seven files.
import { IBMPlexSansArabic_400Regular } from '@expo-google-fonts/ibm-plex-sans-arabic/400Regular';
import { IBMPlexSansArabic_500Medium } from '@expo-google-fonts/ibm-plex-sans-arabic/500Medium';
import { IBMPlexSansArabic_600SemiBold } from '@expo-google-fonts/ibm-plex-sans-arabic/600SemiBold';
import { IBMPlexSansArabic_700Bold } from '@expo-google-fonts/ibm-plex-sans-arabic/700Bold';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import { AuthProvider } from './auth/AuthProvider';
import { ErrorBoundary } from './components/ErrorBoundary';
import RootNavigator from './components/RootNavigator';
import { ToastHost } from './components/ui';
import { LocationProvider } from './location/LocationProvider';
import { configureNotifications } from './notifications/push';

SplashScreen.preventAutoHideAsync().catch(() => {});
configureNotifications();

export default function App() {
  // Icons and the text face must be ready before the first screen renders, otherwise they flash empty or swap.
  const [fontsLoaded, fontError] = useFonts({
    ...Ionicons.font,
    IBMPlexSansArabic_400Regular,
    IBMPlexSansArabic_500Medium,
    IBMPlexSansArabic_600SemiBold,
    IBMPlexSansArabic_700Bold,
  });
  const [assetsReady, setAssetsReady] = useState(false);

  useEffect(() => {
    Asset.loadAsync([require('./assets/images/login-background.jpg')])
      .catch(() => {})
      .finally(() => setAssetsReady(true));
  }, []);

  if ((!fontsLoaded && !fontError) || !assetsReady) return null;

  return (
    <SafeAreaProvider>
      <ErrorBoundary>
        <AuthProvider>
          <LocationProvider>
            <StatusBar style="dark" />
            <RootNavigator />
            <ToastHost />
          </LocationProvider>
        </AuthProvider>
      </ErrorBoundary>
    </SafeAreaProvider>
  );
}
