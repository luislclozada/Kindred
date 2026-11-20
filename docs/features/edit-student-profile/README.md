# Editar el perfil

Un estudiante puede cambiar su nombre y su carrera desde la página **Mi perfil**. El nombre nuevo
se ve en todos los lugares donde aparece: el encabezado, el selector "entrar como", la página
Compañeros de los demás y la lista de cada curso que toma.

## Qué puede hacer un estudiante

1. Abrir **Mi perfil** desde el encabezado.
2. Cambiar su nombre o su carrera y guardar. El correo no se puede cambiar: es su identidad.
3. Ver el nombre nuevo en el encabezado de inmediato, sin recargar.

## Cómo funciona

| Pieza | Dónde | Qué hace |
|---|---|---|
| student-service | `src/Services/StudentService/` | `PUT /api/v1/students/{id}`; el agregado `Student` valida y aplica el cambio |
| matching-service | `src/Services/MatchingService/` | Actualiza el nombre en todas las listas del curso donde está el estudiante |
| frontend | `apps/frontend/src/app/pages/profile/` | La página Mi perfil; al guardar actualiza la sesión |

El nombre vive en dos servicios: la versión real en student-service y una copia en cada lista
del curso de matching-service. Cuando el nombre cambia de verdad, student-service guarda y luego
publica `StudentRenamedIntegrationEvent` en RabbitMQ; matching-service lo escucha y corrige su
copia. Si solo cambia la carrera, no se publica nada: matching-service no guarda la carrera.

## Specs

`student-profiles` y `course-rosters` en `openspec/specs/`. El cambio archivado está en
`openspec/changes/archive/2026-11-20-edit-student-profile/`.
