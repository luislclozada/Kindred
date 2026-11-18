import { HttpErrorResponse } from '@angular/common/http';

import { ApiError } from '../models/api.models';

/** Turns anything thrown by HttpClient into a sentence a student can read. */
export function toErrorMessage(err: unknown): string {
  if (err instanceof HttpErrorResponse) {
    const body = err.error as Partial<ApiError> | string | null;
    if (body && typeof body === 'object' && typeof body.message === 'string') {
      return body.message;
    }
    // The dev proxy answers a bodyless 500 when the gateway is down; real server errors carry a JSON body.
    const bodyless = body === null || typeof body === 'string';
    if (err.status === 0 || [502, 503, 504].includes(err.status) || (err.status === 500 && bodyless)) {
      return 'No se pudo conectar con la API de Kindred. ¿Está corriendo el backend?';
    }
    return `Algo salió mal en el servidor (HTTP ${err.status}).`;
  }
  return 'Algo salió mal.';
}

export function httpStatus(err: unknown): number | null {
  return err instanceof HttpErrorResponse ? err.status : null;
}
