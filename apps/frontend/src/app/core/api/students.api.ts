import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { Classmate, RegisterStudentRequest, Student, StudentSummary } from '../models/api.models';
import { API_BASE } from './api.config';

@Injectable({ providedIn: 'root' })
export class StudentsApi {
  private readonly http = inject(HttpClient);
  private readonly url = `${API_BASE}/students`;

  list(): Observable<StudentSummary[]> {
    return this.http.get<StudentSummary[]>(this.url);
  }

  get(id: string): Observable<Student> {
    return this.http.get<Student>(`${this.url}/${id}`);
  }

  create(body: RegisterStudentRequest): Observable<Student> {
    return this.http.post<Student>(this.url, body);
  }

  enroll(id: string, courseCode: string): Observable<Student> {
    return this.http.post<Student>(`${this.url}/${id}/enrollments`, { courseCode });
  }

  drop(id: string, courseCode: string): Observable<Student> {
    return this.http.delete<Student>(`${this.url}/${id}/enrollments/${encodeURIComponent(courseCode)}`);
  }

  classmates(id: string): Observable<Classmate[]> {
    return this.http.get<Classmate[]>(`${this.url}/${id}/classmates`);
  }
}
