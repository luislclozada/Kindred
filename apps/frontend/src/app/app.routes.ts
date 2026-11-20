import { Routes } from '@angular/router';

import { signedInGuard } from './core/guards/signed-in.guard';

// One routed component per page, lazy loaded. A new page = one entry here + one nav link in the header.
export const routes: Routes = [
  {
    path: '',
    title: 'Kindred - Entrar',
    loadComponent: () => import('./pages/sign-in/sign-in.page').then((m) => m.SignInPage),
  },
  {
    path: 'courses',
    title: 'Kindred - Mis cursos',
    canActivate: [signedInGuard],
    loadComponent: () => import('./pages/courses/courses.page').then((m) => m.CoursesPage),
  },
  {
    path: 'classmates',
    title: 'Kindred - Compañeros',
    canActivate: [signedInGuard],
    loadComponent: () => import('./pages/classmates/classmates.page').then((m) => m.ClassmatesPage),
  },
  {
    path: 'profile',
    title: 'Kindred - Mi perfil',
    canActivate: [signedInGuard],
    loadComponent: () => import('./pages/profile/profile.page').then((m) => m.ProfilePage),
  },
  { path: '**', redirectTo: '' },
];
