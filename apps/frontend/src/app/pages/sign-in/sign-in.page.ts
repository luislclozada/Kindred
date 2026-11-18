import { ChangeDetectionStrategy, Component, OnInit, inject } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';

import { SessionFacade } from '../../core/state/session.facade';
import { StudentsFacade } from '../../core/state/students.facade';
import { initialOf, tintOf } from '../../core/util/tint';
import { BrandImage } from '../../shared/brand-image/brand-image';

@Component({
  selector: 'app-sign-in-page',
  imports: [ReactiveFormsModule, BrandImage],
  templateUrl: './sign-in.page.html',
  styleUrl: './sign-in.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SignInPage implements OnInit {
  protected readonly facade = inject(StudentsFacade);
  private readonly session = inject(SessionFacade);

  protected readonly skeletons = [0, 1, 2, 3, 4, 5];
  protected readonly tintOf = tintOf;
  protected readonly initialOf = initialOf;

  protected readonly form = new FormGroup({
    displayName: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(80)] }),
    email: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.email] }),
    program: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(80)] }),
  });

  ngOnInit(): void {
    this.facade.load();
  }

  protected pick(id: string): void {
    this.session.signIn(id);
  }

  protected showError(name: 'displayName' | 'email' | 'program'): boolean {
    const control = this.form.controls[name];
    return control.invalid && (control.touched || control.dirty);
  }

  protected register(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.facade.register(this.form.getRawValue());
  }
}
