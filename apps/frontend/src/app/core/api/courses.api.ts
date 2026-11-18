import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { Course } from '../models/api.models';
import { API_BASE } from './api.config';

/** Thin HttpClient wrapper. No state, no error handling: that lives in the facade. */
@Injectable({ providedIn: 'root' })
export class CoursesApi {
  private readonly http = inject(HttpClient);

  list(): Observable<Course[]> {
    return this.http.get<Course[]>(`${API_BASE}/courses`);
  }
}
