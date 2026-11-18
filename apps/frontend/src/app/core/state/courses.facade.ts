import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, finalize } from 'rxjs';

import { CoursesApi } from '../api/courses.api';
import { StudentsApi } from '../api/students.api';
import { Course, Student } from '../models/api.models';
import { toErrorMessage } from '../util/api-error';
import { SessionFacade } from './session.facade';

export const MAX_COURSES = 6;

/** The course catalog plus enroll / drop for the signed-in student. */
@Injectable({ providedIn: 'root' })
export class CoursesFacade {
  private readonly coursesApi = inject(CoursesApi);
  private readonly studentsApi = inject(StudentsApi);
  private readonly session = inject(SessionFacade);

  private readonly _courses = signal<Course[]>([]);
  private readonly _loading = signal(false);
  private readonly _error = signal<string | null>(null);
  private readonly _actionError = signal<string | null>(null);
  private readonly _busyCode = signal<string | null>(null);

  readonly courses = this._courses.asReadonly();
  readonly loading = this._loading.asReadonly();
  readonly error = this._error.asReadonly();
  /** Last enroll/drop failure, e.g. "max 6 courses". */
  readonly actionError = this._actionError.asReadonly();
  /** Course code with a request in flight (disables its button). */
  readonly busyCode = this._busyCode.asReadonly();

  readonly maxCourses = MAX_COURSES;
  readonly enrolledCodes = computed(
    () => new Set(this.session.student()?.enrollments.map((e) => e.courseCode) ?? []),
  );
  readonly enrolledTotal = computed(() => this.enrolledCodes().size);

  load(): void {
    this._loading.set(this._courses().length === 0);
    this._error.set(null);
    this.coursesApi
      .list()
      .pipe(finalize(() => this._loading.set(false)))
      .subscribe({
        next: (courses) => this._courses.set(courses),
        error: (err: unknown) => this._error.set(toErrorMessage(err)),
      });
  }

  enroll(courseCode: string): void {
    const id = this.session.studentId();
    if (id) this.run(courseCode, this.studentsApi.enroll(id, courseCode));
  }

  drop(courseCode: string): void {
    const id = this.session.studentId();
    if (id) this.run(courseCode, this.studentsApi.drop(id, courseCode));
  }

  dismissActionError(): void {
    this._actionError.set(null);
  }

  private run(courseCode: string, request: Observable<Student>): void {
    this._busyCode.set(courseCode);
    this._actionError.set(null);
    request.pipe(finalize(() => this._busyCode.set(null))).subscribe({
      next: (student) => {
        this.session.setStudent(student);
        this.refreshCounts();
      },
      error: (err: unknown) => this._actionError.set(toErrorMessage(err)),
    });
  }

  /** Refetch the catalog so "enrolled" counts stay honest. Quiet: no skeleton. */
  private refreshCounts(): void {
    this.coursesApi.list().subscribe({ next: (courses) => this._courses.set(courses) });
  }
}
