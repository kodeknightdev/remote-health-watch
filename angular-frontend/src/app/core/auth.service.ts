import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, catchError, finalize, map, of, shareReplay, tap } from 'rxjs';
import { Role, TokenResponse, User } from './models';

/**
 * Access token lives only in memory; the refresh token is an HttpOnly cookie scoped to /api/auth,
 * so a page reload restores the session through refresh() without the token ever touching storage.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  private readonly accessToken = signal<string | null>(null);
  private readonly expiresAt = signal<number>(0);
  readonly user = signal<User | null>(null);
  readonly isAuthenticated = computed(() => this.user() !== null);
  readonly role = computed(() => this.user()?.role ?? null);

  private refreshInFlight: Observable<string | null> | null = null;

  token(): string | null {
    return this.accessToken();
  }

  tokenExpiringSoon(): boolean {
    return this.expiresAt() - Date.now() < 30_000;
  }

  hasRole(...roles: Role[]): boolean {
    const r = this.role();
    return r !== null && roles.includes(r);
  }

  login(email: string, password: string): Observable<User> {
    return this.http.post<TokenResponse>('/api/auth/login', { email, password }, { withCredentials: true }).pipe(map((r) => this.apply(r)));
  }

  /** Single shared refresh so a burst of 401s doesn't rotate the token several times (which would trip reuse detection). */
  refresh(): Observable<string | null> {
    if (!this.refreshInFlight) {
      this.refreshInFlight = this.http.post<TokenResponse>('/api/auth/refresh', {}, { withCredentials: true }).pipe(
        map((r) => {
          this.apply(r);
          return r.accessToken;
        }),
        catchError(() => {
          this.clear();
          return of(null);
        }),
        finalize(() => (this.refreshInFlight = null)),
        shareReplay(1),
      );
    }
    return this.refreshInFlight;
  }

  logout(): void {
    this.http
      .post('/api/auth/logout', {}, { withCredentials: true })
      .pipe(catchError(() => of(null)))
      .subscribe(() => {
        this.clear();
        this.router.navigate(['/login']);
      });
  }

  /** Called once at startup to pick up an existing session from the refresh cookie. */
  restore(): Observable<boolean> {
    return this.refresh().pipe(map((t) => t !== null));
  }

  private apply(r: TokenResponse): User {
    this.accessToken.set(r.accessToken);
    this.expiresAt.set(new Date(r.expiresAt).getTime());
    this.user.set(r.user);
    return r.user;
  }

  private clear(): void {
    this.accessToken.set(null);
    this.expiresAt.set(0);
    this.user.set(null);
  }
}
