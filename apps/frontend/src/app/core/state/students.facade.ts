import { Injectable, inject, signal } from '@angular/core';
import { finalize } from 'rxjs';

import { StudentsApi } from '../api/students.api';
import { RegisterStudentRequest, StudentSummary } from '../models/api.models';
import { toErrorMessage } from '../util/api-error';
import { SessionFacade } from './session.facade';

/** The student directory (sign-in picker) and registration. */
@Injectable({ providedIn: 'root' })
export class StudentsFacade {
  private readonly api = inject(StudentsApi);
  private readonly session = inject(SessionFacade);

  private readonly _students = signal<StudentSummary[]>([]);
  private readonly _loading = signal(false);
  private readonly _error = signal<string | null>(null);
  private readonly _registering = signal(false);
  private readonly _registerError = signal<string | null>(null);

  readonly students = this._students.asReadonly();
  readonly loading = this._loading.asReadonly();
  readonly error = this._error.asReadonly();
  readonly registering = this._registering.asReadonly();
  readonly registerError = this._registerError.asReadonly();

  load(): void {
    this._loading.set(true);
    this._error.set(null);
    this.api
      .list()
      .pipe(finalize(() => this._loading.set(false)))
      .subscribe({
        next: (students) => this._students.set(students),
        error: (err: unknown) => this._error.set(toErrorMessage(err)),
      });
  }

  register(request: RegisterStudentRequest): void {
    this._registering.set(true);
    this._registerError.set(null);
    this.api
      .create(request)
      .pipe(finalize(() => this._registering.set(false)))
      .subscribe({
        next: (student) => this.session.signInAs(student),
        error: (err: unknown) => this._registerError.set(toErrorMessage(err)),
      });
  }
}
