import { ChangeDetectionStrategy, Component, OnInit, inject } from '@angular/core';

import { CoursesFacade } from '../../core/state/courses.facade';

@Component({
  selector: 'app-courses-page',
  templateUrl: './courses.page.html',
  styleUrl: './courses.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CoursesPage implements OnInit {
  protected readonly facade = inject(CoursesFacade);

  protected readonly skeletons = [0, 1, 2, 3, 4, 5];
  protected readonly pips = Array.from({ length: this.facade.maxCourses }, (_, i) => i);

  ngOnInit(): void {
    this.facade.load();
  }

  protected toggle(code: string, enrolled: boolean): void {
    if (enrolled) this.facade.drop(code);
    else this.facade.enroll(code);
  }
}
