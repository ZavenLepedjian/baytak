import jwtDecode from 'jwt-decode';

export type TokenClaims = {
  role?: string | string[];
  phoneNumber?: string;
  exp?: number;
  [key: string]: unknown;
};

export function decodeToken(token: string | null | undefined): TokenClaims | null {
  if (!token) return null;
  try {
    return jwtDecode<TokenClaims>(token);
  } catch {
    return null;
  }
}

/** Roles are normalised to a single string so existing `.includes('Admin')` checks keep working. */
export function getRoles(token: string | null | undefined): string {
  const role = decodeToken(token)?.role;
  if (Array.isArray(role)) return role.join(',');
  return role ?? '';
}

export function getPhoneNumber(token: string | null | undefined): string {
  return decodeToken(token)?.phoneNumber ?? '';
}

export function isAdmin(roles: string): boolean {
  return roles.includes('Admin');
}

/** True when the token cannot be decoded or its `exp` claim is in the past (30s skew allowed). */
export function isTokenExpired(token: string | null | undefined): boolean {
  const claims = decodeToken(token);
  if (!claims) return true;
  if (typeof claims.exp !== 'number') return false;
  return claims.exp * 1000 < Date.now() - 30_000;
}
