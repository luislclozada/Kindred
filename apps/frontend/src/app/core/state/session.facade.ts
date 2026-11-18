import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, catchError, map, of, tap } from 'rxjs';

import { StudentsApi } from '../api/students.api';
import { Student } from '../models/api.models';
import { httpStatus } from '../util/api-error';

const STORAGE_KEY = 'kindred.studentId';

/** "Who is signed in". There is no auth: we remember a student id. */
@Injectable({ providedIn: 'root' })
export class SessionFacade {
  private readonly api = inject(StudentsApi);
  private readonly router = inject(Router);

  private readonly _studentId = signal<string | null>(readStoredId());
  private readonly _student = signal<Student | null>(null);

  readonly studentId = this._studentId.asReadonly();
  /** Full record of the signed-in student (enrollments included). Null until loaded. */
  readonly student = this._student.asReadonly();
  readonly signedIn = computed(() => this._studentId() !== null);

  /** Pick an existing student. The route guard loads their record. */
  signIn(id: string): void {
    if (id !== this._studentId()) {
      this._student.set(null);
    }
    this.persist(id);
    void this.router.navigateByUrl('/courses');
  }

  /** Sign in as a student we already have in full (e.g. just registered). */
  signInAs(student: Student): void {
    this._student.set(student);
    this.persist(student.id);
    void this.router.navigateByUrl('/courses');
  }

  signOut(): void {
    this._student.set(null);
    this.persist(null);
    void this.router.navigateByUrl('/');
  }

  /** Replace the cached record after an enroll / drop. */
  setStudent(student: Student): void {
    this._student.set(student);
  }

  /** Used by the route guard: true when a student is signed in and loaded. */
  ensureLoaded(): Observable<boolean> {
    const id = this._studentId();
    if (id === null) return of(false);
    if (this._student()?.id === id) return of(true);

    return this.api.get(id).pipe(
      tap((student) => this._student.set(student)),
      map(() => true),
      catchError((err: unknown) => {
        // Stale id (e.g. the database was reseeded): forget it. Other errors keep it.
        if (httpStatus(err) === 404) this.persist(null);
        return of(false);
      }),
    );
  }

  private persist(id: string | null): void {
    this._studentId.set(id);
    try {
      if (id === null) localStorage.removeItem(STORAGE_KEY);
      else localStorage.setItem(STORAGE_KEY, id);
    } catch {
      // storage unavailable: session lives in memory only
    }
  }
}

function readStoredId(): string | null {
  try {
    return localStorage.getItem(STORAGE_KEY);
  } catch {
    return null;
  }
}
