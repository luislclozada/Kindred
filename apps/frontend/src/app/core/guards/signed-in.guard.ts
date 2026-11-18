import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { map } from 'rxjs';

import { SessionFacade } from '../state/session.facade';

/** Sends visitors who are not signed in (or whose student no longer exists) back to "/". */
export const signedInGuard: CanActivateFn = () => {
  const router = inject(Router);
  return inject(SessionFacade)
    .ensureLoaded()
    .pipe(map((ok) => ok || router.createUrlTree(['/'])));
};
