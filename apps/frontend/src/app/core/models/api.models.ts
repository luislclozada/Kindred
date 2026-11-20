// DTOs mirroring the gateway contract (/api/v1, camelCase JSON).

export interface Course {
  code: string;
  title: string;
  department: string;
  enrolledCount: number;
}

export interface StudentSummary {
  id: string;
  displayName: string;
  program: string;
  courseCount: number;
}

export interface Enrollment {
  courseCode: string;
  enrolledAt: string;
}

export interface Student {
  id: string;
  displayName: string;
  email: string;
  program: string;
  enrollments: Enrollment[];
}

export interface RegisterStudentRequest {
  displayName: string;
  email: string;
  program: string;
}

export interface UpdateProfileRequest {
  displayName: string;
  program: string;
}

export interface Classmate {
  studentId: string;
  displayName: string;
  program: string;
  sharedCourses: string[];
  sharedCount: number;
}

export interface CourseRoster {
  courseCode: string;
  students: { studentId: string; displayName: string }[];
}

/** Error body returned by the API on 400 / 404 / 409. */
export interface ApiError {
  code: string;
  message: string;
}
