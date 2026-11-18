import { ChangeDetectionStrategy, Component, OnInit, inject } from '@angular/core';
import { RouterLink } from '@angular/router';

import { ClassmatesFacade } from '../../core/state/classmates.facade';
import { initialOf, tintOf } from '../../core/util/tint';
import { BrandImage } from '../../shared/brand-image/brand-image';

@Component({
  selector: 'app-classmates-page',
  imports: [RouterLink, BrandImage],
  templateUrl: './classmates.page.html',
  styleUrl: './classmates.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ClassmatesPage implements OnInit {
  protected readonly facade = inject(ClassmatesFacade);

  protected readonly skeletons = [0, 1, 2];
  protected readonly tintOf = tintOf;
  protected readonly initialOf = initialOf;

  ngOnInit(): void {
    this.facade.load();
  }
}
