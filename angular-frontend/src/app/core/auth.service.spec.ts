import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from './auth.service';
import { TokenResponse } from './models';

const token = (t: string): TokenResponse => ({
  accessToken: t,
  expiresAt: new Date(Date.now() + 600_000).toISOString(),
  user: { id: 'u1', email: 'nurse@remotehealthwatch.local', displayName: 'Sam', role: 'Nurse', isOnCall: false },
});

describe('AuthService', () => {
  let auth: AuthService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    auth = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('keeps the access token in memory only', () => {
    auth.login('nurse@remotehealthwatch.local', 'pw').subscribe();
    const req = http.expectOne('/api/auth/login');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(token('abc'));
    expect(auth.token()).toBe('abc');
    expect(auth.role()).toBe('Nurse');
    expect(localStorage.getItem('access_token')).toBeNull();
    expect(sessionStorage.length).toBe(0);
  });

  it('shares one refresh between concurrent callers so the rotated token is not reused', () => {
    const seen: (string | null)[] = [];
    auth.refresh().subscribe((t) => seen.push(t));
    auth.refresh().subscribe((t) => seen.push(t));
    http.expectOne('/api/auth/refresh').flush(token('fresh'));
    expect(seen).toEqual(['fresh', 'fresh']);
  });

  it('clears the session when refresh is rejected', () => {
    auth.login('a@b.c', 'pw').subscribe();
    http.expectOne('/api/auth/login').flush(token('old'));
    let result: string | null | undefined;
    auth.refresh().subscribe((t) => (result = t));
    http.expectOne('/api/auth/refresh').flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(result).toBeNull();
    expect(auth.isAuthenticated()).toBeFalse();
  });
});
