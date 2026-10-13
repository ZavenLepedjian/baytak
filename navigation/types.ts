import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import type { NavigatorScreenParams } from '@react-navigation/native';

export type BuildingChoice = { id: number | string; label: string };
export type ApartmentChoice = { id: number | string; appartmentNumber: string };

export type TabParamList = {
  Home: undefined;
  Collect: undefined;
  Manage: undefined;
  Account: undefined;
};

export type RootStackParamList = {
  // Signed out
  Login: undefined;
  LoginVerification: { phoneNumber: string; requestAdmin?: boolean };
  // Waiting for an administrator account to be approved
  AdminPending: undefined;
  // Signed in
  Tabs: NavigatorScreenParams<TabParamList> | undefined;
  Profile: undefined;
  JoinBuilding: { building?: BuildingChoice } | undefined;
  Buildings: undefined;
  Receipts: undefined;
  Wallet: undefined;
  // Admin
  Apartments: undefined;
  PendingApartments: undefined;
  ApartmentReceipts: { id: number | string; appartmentNumber: string };
  CreateReceipt: { apartments?: ApartmentChoice[] } | undefined;
  SelectApartments: { selected: ApartmentChoice[] };
  SetupReceipt: undefined;
  SetupBuilding: { latitude?: number; longitude?: number } | undefined;
  BuildingLocation: { latitude?: number; longitude?: number } | undefined;
};

export type ScreenProps<T extends keyof RootStackParamList> = NativeStackScreenProps<RootStackParamList, T>;
