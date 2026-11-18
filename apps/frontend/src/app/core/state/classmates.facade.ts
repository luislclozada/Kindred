import { Injectable, computed, inject, signal } from '@angular/core';
import { finalize, forkJoin } from 'rxjs';

import { CoursesApi } from '../api/courses.api';
import { StudentsApi } from '../api/students.api';
import { Classmate, Course } from '../models/api.models';
import { toErrorMessage } from '../util/api-error';
import { SessionFacade } from './session.facade';

export interface ClassmateGroup {
  course: Course;
  members: Classmate[];
}

/** Classmates of the signed-in student, grouped by each of their courses. */
@Injectable({ providedIn: 'root' })
export class ClassmatesFacade {
  private readonly studentsApi = inject(StudentsApi);
  private readonly coursesApi = inject(CoursesApi);
  private readonly session = inject(SessionFacade);

  private readonly _classmates = signal<Classmate[]>([]);
  private readonly _catalog = signal<Course[]>([]);
  private readonly _loading = signal(false);
  private readonly _error = signal<string | null>(null);

  readonly loading = this._loading.asReadonly();
  readonly error = this._error.asReadonly();
  readonly hasCourses = computed(() => (this.session.student()?.enrollments.length ?? 0) > 0);
  readonly hasClassmates = computed(() => this._classmates().length > 0);

  /** One group per enrolled course; members sorted by shared count, highest first. */
  readonly groups = computed<ClassmateGroup[]>(() => {
    const byCode = new Map(this._catalog().map((c) => [c.code, c]));
    const classmates = this._classmates();
    return (this.session.student()?.enrollments ?? [])
      .map(({ courseCode }) => ({
        course: byCode.get(courseCode) ?? { code: courseCode, title: courseCode, department: '', enrolledCount: 0 },
        members: classmates
          .filter((c) => c.sharedCourses.includes(courseCode))
          .sort((a, b) => b.sharedCount - a.sharedCount || a.displayName.localeCompare(b.displayName)),
      }))
      .sort((a, b) => a.course.code.localeCompare(b.course.code));
  });

  load(): void {
    const id = this.session.studentId();
    if (!id) return;
    this._loading.set(true);
    this._error.set(null);
    // Course titles come from the catalog; the classmates endpoint only returns codes.
    forkJoin({ classmates: this.studentsApi.classmates(id), catalog: this.coursesApi.list() })
      .pipe(finalize(() => this._loading.set(false)))
      .subscribe({
        next: ({ classmates, catalog }) => {
          this._classmates.set(classmates);
          this._catalog.set(catalog);
        },
        error: (err: unknown) => this._error.set(toErrorMessage(err)),
      });
  }
}
