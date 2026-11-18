import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { CourseRoster } from '../models/api.models';
import { API_BASE } from './api.config';

@Injectable({ providedIn: 'root' })
export class MatchingApi {
  private readonly http = inject(HttpClient);

  roster(courseCode: string): Observable<CourseRoster> {
    return this.http.get<CourseRoster>(`${API_BASE}/matching/courses/${encodeURIComponent(courseCode)}/roster`);
  }
}
