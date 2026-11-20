import { ChangeDetectionStrategy, Component, effect, inject } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';

import { ProfileFacade } from '../../core/state/profile.facade';

@Component({
  selector: 'app-profile-page',
  imports: [ReactiveFormsModule],
  templateUrl: './profile.page.html',
  styleUrl: './profile.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProfilePage {
  protected readonly facade = inject(ProfileFacade);

  protected readonly form = new FormGroup({
    displayName: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(80)] }),
    program: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(80)] }),
  });

  constructor() {
    // Prefill from the signed-in student; runs again only when the session record is replaced
    // (after a successful save), so what the user typed survives a failed save.
    effect(() => {
      const student = this.facade.student();
      if (student) {
        this.form.setValue({ displayName: student.displayName, program: student.program });
      }
    });
  }

  protected showError(name: 'displayName' | 'program'): boolean {
    const control = this.form.controls[name];
    return control.invalid && (control.touched || control.dirty);
  }

  protected save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.facade.save(this.form.getRawValue());
  }
}
