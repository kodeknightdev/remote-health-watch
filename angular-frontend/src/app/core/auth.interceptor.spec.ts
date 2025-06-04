import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';

describe('authInterceptor', () => {
  let http: HttpClient;
  let backend: HttpTestingController;
  let auth: AuthService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([authInterceptor])), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
  });

  const tokenBody = (t: string) => ({
    accessToken: t,
    expiresAt: new Date(Date.now() + 60_000).toISOString(),
    user: { id: 'u', email: 'x@y.z', displayName: 'X', role: 'Doctor', isOnCall: false },
  });

  it('adds the bearer token to api calls', () => {
    auth.login('x@y.z', 'pw').subscribe();
    backend.expectOne('/api/auth/login').flush(tokenBody('t1'));
    http.get('/api/patients').subscribe();
    expect(backend.expectOne('/api/patients').request.headers.get('Authorization')).toBe('Bearer t1');
  });

  it('refreshes once on 401 and replays the request with the new token', () => {
    auth.login('x@y.z', 'pw').subscribe();
    backend.expectOne('/api/auth/login').flush(tokenBody('expired'));

    let body: unknown;
    http.get('/api/alerts').subscribe((b) => (body = b));
    backend.expectOne('/api/alerts').flush({}, { status: 401, statusText: 'Unauthorized' });
    backend.expectOne('/api/auth/refresh').flush(tokenBody('t2'));
    const retry = backend.expectOne('/api/alerts');
    expect(retry.request.headers.get('Authorization')).toBe('Bearer t2');
    retry.flush([{ id: 'a1' }]);
    expect(body).toEqual([{ id: 'a1' }]);
  });
});
