import { Injectable, inject, signal } from '@angular/core';
import { finalize } from 'rxjs';

import { StudentsApi } from '../api/students.api';
import { UpdateProfileRequest } from '../models/api.models';
import { toErrorMessage } from '../util/api-error';
import { SessionFacade } from './session.facade';

/** Edit the signed-in student's name and program. */
@Injectable({ providedIn: 'root' })
export class ProfileFacade {
  private readonly studentsApi = inject(StudentsApi);
  private readonly session = inject(SessionFacade);

  private readonly _saving = signal(false);
  private readonly _saved = signal(false);
  private readonly _error = signal<string | null>(null);

  /** The signed-in student (the form prefill). Null until the session has loaded it. */
  readonly student = this.session.student;
  readonly saving = this._saving.asReadonly();
  /** True after a successful save, until the next edit or save. */
  readonly saved = this._saved.asReadonly();
  readonly error = this._error.asReadonly();

  save(values: UpdateProfileRequest): void {
    const id = this.session.studentId();
    if (!id) return;
    this._saving.set(true);
    this._saved.set(false);
    this._error.set(null);
    this.studentsApi
      .updateProfile(id, values)
      .pipe(finalize(() => this._saving.set(false)))
      .subscribe({
        next: (student) => {
          // The header reads the session, so it shows the new name right away.
          this.session.setStudent(student);
          this._saved.set(true);
        },
        error: (err: unknown) => this._error.set(toErrorMessage(err)),
      });
  }

  /** Hide the previous outcome once the user starts editing again. */
  clearStatus(): void {
    this._saved.set(false);
    this._error.set(null);
  }
}
