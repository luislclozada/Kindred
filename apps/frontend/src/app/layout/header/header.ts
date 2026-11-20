import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';

import { SessionFacade } from '../../core/state/session.facade';
import { ThemeFacade } from '../../core/state/theme.facade';
import { BrandImage } from '../../shared/brand-image/brand-image';

interface NavItem {
  label: string;
  path: string;
}

@Component({
  selector: 'app-header',
  imports: [RouterLink, RouterLinkActive, BrandImage],
  templateUrl: './header.html',
  styleUrl: './header.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AppHeader {
  protected readonly session = inject(SessionFacade);
  protected readonly theme = inject(ThemeFacade);

  /** Add a page: add an entry here and a route in app.routes.ts. */
  protected readonly nav: NavItem[] = [
    { label: 'Mis cursos', path: '/courses' },
    { label: 'Compañeros', path: '/classmates' },
    { label: 'Mi perfil', path: '/profile' },
  ];
}
