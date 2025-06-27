import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, switchMap, throwError } from 'rxjs';
import { AuthService } from './auth.service';

const isAuthCall = (req: HttpRequest<unknown>) => req.url.startsWith('/api/auth/');

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (isAuthCall(req)) return next(req);

  const withToken = (token: string | null) => (token ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req);

  return next(withToken(auth.token())).pipe(
    catchError((err: unknown) => {
      if (!(err instanceof HttpErrorResponse) || err.status !== 401) return throwError(() => err);
      return auth.refresh().pipe(
        switchMap((token) => {
          if (!token) {
            router.navigate(['/login']);
            return throwError(() => err);
          }
          return next(withToken(token));
        }),
      );
    }),
  );
};
