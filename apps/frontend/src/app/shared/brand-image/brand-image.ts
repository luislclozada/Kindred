import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';

/**
 * Slot for an AI-generated brand image. Shows the image from assets/brand/ when it exists,
 * otherwise a patterned placeholder so the layout still looks intentional.
 */
@Component({
  selector: 'app-brand-image',
  templateUrl: './brand-image.html',
  styleUrl: './brand-image.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[style.aspect-ratio]': 'ratio()' },
})
export class BrandImage {
  /** Path relative to the site root, e.g. "assets/brand/hero.png". */
  readonly src = input.required<string>();
  /** Alt text. Empty string marks the image as decorative. */
  readonly alt = input('');
  /** CSS aspect-ratio, e.g. "1 / 1". */
  readonly ratio = input('1 / 1');
  /** Single character shown in the placeholder (used for the logo mark). */
  readonly mark = input('');

  protected readonly failed = signal(false);
  protected readonly fileName = computed(() => this.src().split('/').pop() ?? this.src());

  protected onError(): void {
    this.failed.set(true);
  }
}
