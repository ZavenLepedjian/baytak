import React, { useEffect } from 'react';
import { Pressable } from 'react-native';
import { DefaultTheme, NavigationContainer, Theme } from '@react-navigation/native';
import { createNativeStackNavigator, NativeStackNavigationOptions } from '@react-navigation/native-stack';
import { createBottomTabNavigator } from '@react-navigation/bottom-tabs';
import { Ionicons } from '@expo/vector-icons';
import * as SplashScreen from 'expo-splash-screen';
import { useAuth } from '../auth/AuthProvider';
import { FEATURES } from '../config';
import { registerForPushNotifications } from '../notifications/push';
import type { RootStackParamList, TabParamList } from '../navigation/types';
import { colors, family, fonts, hitSlop } from '../theme';

import LoginScreen from '../screens/Auth/Login';
import LoginVerification from '../screens/Auth/LoginVerification';
import AdminPending from '../screens/Auth/AdminPending';
import Dashboard from '../screens/Dashboard';
import Account from '../screens/Account';
import Receipts from '../screens/AppartmentAllReceipts';
import Wallet from '../screens/Wallet';
import Profile from '../screens/User/Profile';
import JoinBuilding from '../screens/User/AppartmentRequest';
import Buildings from '../screens/User/Buildings';
import Manage from '../screens/Admin/Manage';
import Collection from '../screens/Admin/Collection';
import Apartments from '../screens/Admin/Appartments';
import PendingApartments from '../screens/Admin/AddAppartment';
import ApartmentReceipts from '../screens/Admin/ListOfPaymentsAndReciepts';
import CreateReceipt from '../screens/Admin/AddReceipt';
import SelectApartments from '../screens/Admin/SimpleAppartmentList';
import SetupReceipt from '../screens/Admin/SetupReceipt';
import SetupBuilding from '../screens/Admin/AddBuilding';
import BuildingLocation from '../screens/Admin/BuildingLocation';

const Stack = createNativeStackNavigator<RootStackParamList>();
const Tab = createBottomTabNavigator<TabParamList>();

const navTheme: Theme = {
  ...DefaultTheme,
  colors: {
    ...DefaultTheme.colors,
    primary: colors.primary,
    background: colors.bg,
    card: colors.surface,
    text: colors.text,
    border: colors.border,
    notification: colors.danger,
  },
};

const stackScreenOptions: NativeStackNavigationOptions = {
  headerShadowVisible: false,
  headerTintColor: colors.primary,
  headerBackButtonDisplayMode: 'minimal',
  headerTitleStyle: { ...fonts.heading, color: colors.text },
  headerStyle: { backgroundColor: colors.bg },
  contentStyle: { backgroundColor: colors.bg },
};

const tabIcons: Record<keyof TabParamList, { on: keyof typeof Ionicons.glyphMap; off: keyof typeof Ionicons.glyphMap }> = {
  Home: { on: 'home', off: 'home-outline' },
  Collect: { on: 'albums', off: 'albums-outline' },
  Manage: { on: 'briefcase', off: 'briefcase-outline' },
  Account: { on: 'person', off: 'person-outline' },
};

function Tabs() {
  const { isAdmin } = useAuth();
  return (
    <Tab.Navigator
      screenOptions={({ route }) => ({
        headerShown: false,
        tabBarActiveTintColor: colors.primary,
        tabBarInactiveTintColor: colors.textFaint,
        tabBarLabelStyle: { fontFamily: family.semibold, fontSize: 12 },
        tabBarStyle: { backgroundColor: colors.surface, borderTopColor: colors.border },
        tabBarIcon: ({ focused, color, size }) => {
          const icon = tabIcons[route.name];
          return <Ionicons name={focused ? icon.on : icon.off} size={size} color={color} />;
        },
      })}
    >
      <Tab.Screen name="Home" component={Dashboard} options={{ title: 'Home' }} />
      {isAdmin ? (
        <Tab.Screen
          name="Collect"
          component={Collection}
          options={{
            title: 'Collection',
            tabBarLabel: 'Collect',
            headerShown: true,
            headerShadowVisible: false,
            headerStyle: { backgroundColor: colors.primary },
            headerTintColor: colors.onInk,
            headerTitleStyle: { fontFamily: family.semibold, fontSize: 17 },
          }}
        />
      ) : null}
      {isAdmin ? <Tab.Screen name="Manage" component={Manage} options={{ title: 'Manage' }} /> : null}
      <Tab.Screen name="Account" component={Account} options={{ title: 'Account' }} />
    </Tab.Navigator>
  );
}

/** Registers the device for push notifications once the user is fully signed in. */
function PushRegistrar() {
  const { token } = useAuth();
  useEffect(() => {
    if (token) void registerForPushNotifications(token);
  }, [token]);
  return null;
}

export default function RootNavigator() {
  const { status, needsAdminApproval, isAdmin } = useAuth();

  // Keep the native splash screen up until the saved session has been restored.
  useEffect(() => {
    if (status !== 'loading') SplashScreen.hideAsync().catch(() => {});
  }, [status]);

  if (status === 'loading') return null;

  return (
    <NavigationContainer theme={navTheme}>
      {status === 'signedIn' && !needsAdminApproval ? <PushRegistrar /> : null}
      <Stack.Navigator screenOptions={stackScreenOptions}>
        {status === 'signedOut' ? (
          <>
            <Stack.Screen name="Login" component={LoginScreen} options={{ headerShown: false }} />
            <Stack.Screen name="LoginVerification" component={LoginVerification} options={{ headerShown: false }} />
          </>
        ) : needsAdminApproval ? (
          <Stack.Screen name="AdminPending" component={AdminPending} options={{ headerShown: false }} />
        ) : (
          <>
            <Stack.Screen name="Tabs" component={Tabs} options={{ headerShown: false }} />
            <Stack.Screen name="Profile" component={Profile} options={{ title: 'Edit profile' }} />
            <Stack.Screen name="JoinBuilding" component={JoinBuilding} options={{ title: 'My apartment' }} />
            <Stack.Screen name="Buildings" component={Buildings} options={{ title: 'Choose a building' }} />
            <Stack.Screen name="Receipts" component={Receipts} options={{ title: 'My receipts' }} />
            {FEATURES.wallet ? <Stack.Screen name="Wallet" component={Wallet} options={{ title: 'Wallet' }} /> : null}

            {isAdmin ? (
              <>
                <Stack.Screen
                  name="Apartments"
                  component={Apartments}
                  options={({ navigation }) => ({
                    title: 'Apartments',
                    headerRight: () => (
                      <Pressable accessibilityRole="button" accessibilityLabel="Join requests" hitSlop={hitSlop} onPress={() => navigation.navigate('PendingApartments')}>
                        <Ionicons name="person-add-outline" size={24} color={colors.primary} />
                      </Pressable>
                    ),
                  })}
                />
                <Stack.Screen name="PendingApartments" component={PendingApartments} options={{ title: 'Join requests' }} />
                <Stack.Screen name="ApartmentReceipts" component={ApartmentReceipts} options={({ route }) => ({ title: route.params.appartmentNumber })} />
                <Stack.Screen name="CreateReceipt" component={CreateReceipt} options={{ title: 'New receipt' }} />
                <Stack.Screen name="SelectApartments" component={SelectApartments} options={{ title: 'Select apartments' }} />
                <Stack.Screen name="SetupReceipt" component={SetupReceipt} options={{ title: 'Receipt format' }} />
                <Stack.Screen name="SetupBuilding" component={SetupBuilding} options={{ title: 'Building details' }} />
                <Stack.Screen name="BuildingLocation" component={BuildingLocation} options={{ title: 'Building location' }} />
              </>
            ) : null}
          </>
        )}
      </Stack.Navigator>
    </NavigationContainer>
  );
}
