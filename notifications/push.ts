import { Platform } from 'react-native';
import * as Notifications from 'expo-notifications';
import { apiRequest } from '../api/client';
import { EAS_PROJECT_ID } from '../config';

/** Call once at startup. */
export function configureNotifications() {
  Notifications.setNotificationHandler({
    handleNotification: async () => ({
      shouldShowBanner: true,
      shouldShowList: true,
      shouldPlaySound: false,
      shouldSetBadge: true,
    }),
  });
}

let lastRegisteredToken: string | null = null;

/**
 * Requests permission, obtains the Expo push token and registers it with the backend.
 * Fails silently: push is a convenience and must never break sign-in.
 */
export async function registerForPushNotifications(userToken: string): Promise<void> {
  if (lastRegisteredToken === userToken) return;
  try {
    if (Platform.OS === 'android') {
      await Notifications.setNotificationChannelAsync('default', {
        name: 'Default',
        importance: Notifications.AndroidImportance.DEFAULT,
        lightColor: '#009999',
      });
    }

    const existing = await Notifications.getPermissionsAsync();
    let status = existing.status;
    if (status !== 'granted') {
      status = (await Notifications.requestPermissionsAsync()).status;
    }
    if (status !== 'granted') return;

    const push = await Notifications.getExpoPushTokenAsync({ projectId: EAS_PROJECT_ID });
    const res = await apiRequest('setup/notification', {
      method: 'POST',
      token: userToken,
      body: { notificationToken: push.data },
    });
    if (res.ok) lastRegisteredToken = userToken;
  } catch {
    // Simulators, missing Google services, offline: ignore.
  }
}
