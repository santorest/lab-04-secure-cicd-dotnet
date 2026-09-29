---
title: "Pipeline CI/CD seguro para una API .NET"
id: "lab-04-secure-cicd-dotnet"
category: "DevSecOps y AppSec"
type: "Laboratorio"
status: "completado"
date: "2026-09-29"
time_to_reproduce: "2–3 horas (fork, activar Actions, aplicar el ruleset)"
skills: [.NET, ASP.NET Core, GitHub Actions, CodeQL, Semgrep, gitleaks, Trivy, OWASP ZAP, Checkov, CycloneDX, cosign]
frameworks: [NIST SSDF (SP 800-218), OWASP SAMM, CIS Controls v8, SLSA]
repo: "https://github.com/santorest/lab-04-secure-cicd-dotnet"
bundle: "Publicado en el sitio del portafolio con su checksum SHA-256"
---

# Pipeline CI/CD seguro para una API .NET

> **En resumen** — Una pequeña API de tickets en ASP.NET Core y un pipeline de GitHub Actions en el que cada
> pull request debe superar siete controles de seguridad (pruebas, dependencias vulnerables, SAST, secretos,
> configuración de workflows y Dockerfile, CVE del contenedor y un escaneo DAST autenticado) antes de que un
> ruleset de la rama permita integrarlo; cada integración publica una imagen firmada con SBOM y procedencia de
> compilación que cualquiera puede verificar. Seis pull requests de demostración, rotos a propósito, muestran
> cada control bloqueando un problema real.
> **Todo se ejecutó de verdad en runners de GitHub; todas las cifras salen de esas ejecuciones.**

| | |
|---|---|
| **Rol** | Ingeniero DevSecOps que construye el pipeline de entrega de un equipo pequeño |
| **Entorno** | Repositorio público de GitHub, runners Ubuntu de GitHub, GitHub Container Registry |
| **Herramientas** | .NET 10, xUnit, CodeQL, Semgrep, gitleaks, Checkov, actionlint, zizmor, Trivy, OWASP ZAP, CycloneDX, cosign |
| **Entregable** | API + pruebas, `ci.yml` / `codeql.yml` / `release.yml`, ruleset de rama, registro de excepciones, PR de demostración, resultados |

---

## 1. Problema

- **Contexto:** un equipo pequeño publica una API interna de mesa de ayuda. La revisión de código no detecta
  un paquete transitivo vulnerable, un secreto pegado en un archivo de configuración, una consulta inyectable
  ni una imagen base desactualizada, y nadie puede demostrar qué compilación produjo la imagen en producción.
- **Objetivos:**
    1. Bloquear esos problemas en el pull request, automáticamente, antes de que lleguen a `main`.
    2. Hacer explícitas las excepciones: cada hallazgo aceptado tiene motivo, responsable y fecha de vencimiento.
    3. Hacer verificable cada versión: firmada, con lista de materiales y procedencia de compilación.
- **Restricciones:** solo herramientas gratuitas, sin claves de firma de larga duración y sin secretos
  disponibles para los trabajos de pull requests.

## 2. La API

Una API de tickets en ASP.NET Core 10 (minimal APIs, EF Core con SQLite):

- `POST /api/auth/login` emite un JWT de 15 minutos (HMAC-SHA256). Las contraseñas se guardan con el hash de
  ASP.NET Core Identity; los inicios de sesión están limitados (5 por minuto por IP) y las cuentas se bloquean
  tras 5 fallos. Un usuario inexistente y una contraseña incorrecta reciben la misma respuesta.
- Los usuarios ven y crean **sus propios** tickets; los agentes ven todos y los mueven por
  `open → in_progress → resolved → closed`, un paso a la vez. El ticket de otro usuario responde **404**, no 403,
  para que no se puedan sondear los identificadores.
- Cada respuesta lleva `Content-Security-Policy: default-src 'none'`, `X-Content-Type-Options`,
  `Referrer-Policy`, `Cross-Origin-Resource-Policy`, sin encabezado `Server`, y `Cache-Control: no-store` bajo
  `/api`. Los errores son *problem details* (RFC 9457) sin trazas; los cuerpos de más de 64 KB reciben 413.
- El contenedor usa la imagen **chiseled** de .NET de Microsoft (sin shell ni gestor de paquetes), con usuario
  no root y sistema de archivos raíz de **solo lectura**; solo `/data` (el archivo SQLite) admite escritura.
- **60 pruebas .NET** (15 unitarias, 29 de integración, 16 de política del repositorio), incluidos tokens
  falsificados, expirados, con `alg: none`, emisor o audiencia incorrectos, acceso entre usuarios y paginación
  inválida.

## 3. Controles del pull request

| Control | Herramienta | Bloquea cuando |
|---|---|---|
| build-test | `dotnet build` (advertencias como errores) + xUnit | hay una advertencia de compilación o una prueba falla |
| dependencies | auditoría NuGet al restaurar; Dependabot | hay un aviso High/Critical, directo o transitivo |
| sast | Semgrep (`p/csharp`, `p/secrets`); CodeQL `security-extended` en su propio workflow | un hallazgo ERROR de Semgrep; una alerta High+ de CodeQL (exigida por el ruleset) |
| secrets | gitleaks, historial completo del commit evaluado | cualquier hallazgo |
| config | Checkov (Dockerfile, workflows), actionlint, zizmor | cualquier hallazgo |
| container | compilación de la imagen, prueba de humo en solo lectura, Trivy | un CVE High/Critical con corrección disponible |
| dast | escaneo de API con OWASP ZAP, autenticado y guiado por el documento OpenAPI | una alerta High, o una Medium de la lista de fallo |

El pipeline también se protege a sí mismo: cada acción de terceros está fijada a un commit SHA completo y cada
imagen de escáner a un digest (Dependabot propone actualizaciones), los workflows tienen permisos de solo
lectura por defecto, los checkouts no conservan credenciales y los trabajos de pull request no reciben
secretos: el trabajo DAST genera su propia clave de firma y usuario de prueba desechables en cada ejecución.

**Ruleset en `main`:** cambios solo mediante pull request; los 8 controles en verde y al día; ninguna alerta
High+ de CodeQL; sin *force-push* ni borrado; historial lineal. Tiene 0 aprobaciones requeridas porque un
único mantenedor no puede aprobar su propio pull request; un equipo fijaría 1 o más aprobaciones y revisión
de CODEOWNERS. Un `git push` directo a `main` se rechaza:

```
remote: error: GH013: Repository rule violations found for refs/heads/main.
remote: - Changes must be made through a pull request.
remote: - 8 of 8 required status checks are expected.
```

## 4. Proceso de excepciones

Un hallazgo solo puede aceptarse en lugar de corregirse con una fila en `security/EXCEPTIONS.md`: herramienta,
regla, alcance, motivo, responsable y fecha de vencimiento. Una prueba de política del repositorio se ejecuta
en `build-test` y hace fallar el PR cuando existe una supresión en el formato propio de cualquier herramienta
(NuGet, Trivy, Semgrep, ZAP, Checkov, gitleaks) sin fila en el registro, cuando una fila no tiene supresión o
cuando una fila venció. Hoy el registro tiene una entrada: **EX-001**, Checkov `CKV_DOCKER_2` (sin
`HEALTHCHECK`): la imagen chiseled no tiene shell ni curl para ejecutarlo, así que `/health` se comprueba
desde fuera; vence el 2027-03-31.

## 5. Publicación y verificación

En cada integración a `main`, `release.yml` publica la imagen en GHCR con el SHA del commit como etiqueta,
genera dos SBOM CycloneDX (dependencias NuGet e imagen completa), firma la imagen con **cosign sin claves**
(la identidad OIDC de GitHub: no hay claves que guardar ni filtrar), adjunta el SBOM como atestación firmada,
registra la **procedencia de compilación** y luego lo verifica todo como lo haría un tercero:

```bash
cosign verify ghcr.io/santorest/lab-04-secure-cicd-dotnet@<digest> \
  --certificate-identity-regexp '^https://github.com/santorest/lab-04-secure-cicd-dotnet/.github/workflows/release.yml@refs/heads/main$' \
  --certificate-oidc-issuer https://token.actions.githubusercontent.com
gh attestation verify oci://ghcr.io/santorest/lab-04-secure-cicd-dotnet@<digest> --owner santorest
```

La primera versión también se verificó desde otro equipo Windows con cosign v3.1.3
([results/cosign-verify.txt](results/cosign-verify.txt)); la misma comprobación con la identidad de otro
repositorio falla, como debe ser.

## 6. Resultados

Todas las cifras salen de [results/results.json](results/results.json), generado desde la API pública de
GitHub con `tools/collect_results.py` el 2026-09-29.

**Tiempo del pipeline** (ejecuciones exitosas en `main`):

| | Mediana | Ejecuciones |
|---|---|---|
| Pipeline completo del PR (tiempo real) | 275 s (4 min 35 s) | 5 |
| build-test | 32 s | 5 |
| dependencies | 27,5 s | 4 |
| sast (Semgrep) | 28,5 s | 4 |
| secrets | 11 s | 4 |
| config | 28,5 s | 4 |
| container | 66,5 s | 4 |
| dast (ZAP) | 167 s | 4 |
| Publicación (firma, SBOM, procedencia, verificación) | 91 s | 4 |

**Pull requests de demostración** (cada uno añade un problema deliberado; cerrados sin integrar; detalle en
[docs/demo-prs.md](docs/demo-prs.md)):

| Demo | Problema deliberado | Detenido por |
|---|---|---|
| — | Par de claves estilo AWS (aleatorias, nunca válidas) | **Protección de push de GitHub**, en el `git push`, antes de CI |
| #7 | Clave de API genérica en un archivo de configuración | secrets (gitleaks) |
| #3 | `System.Text.Json` 8.0.4 (CVE-2024-43485, High) | dependencies (auditoría NuGet), además de build-test y CodeQL |
| #4 | SQL construido concatenando cadenas | build-test: analizador de EF Core EF1003 |
| #8 | Lo mismo, con la advertencia del analizador silenciada | CodeQL `cs/sql-injection` (High) + ZAP SQL Injection (High); PR **bloqueado** |
| #5 | Imagen base `aspnet:10.0.0-noble` sin parches | container: 15 CVE High corregibles (Trivy) |
| #6 | Encabezado `Content-Security-Policy` eliminado | build-test: prueba de integración (no hizo falta el DAST) |

**Hallazgos en el código real** y qué pasó con ellos:

| Hallazgo | Herramienta | Resultado |
|---|---|---|
| Falta el encabezado `Cross-Origin-Resource-Policy` (regla 90004) | ZAP | **Corregido**, con prueba; el escaneo siguiente pasó 118 de 118 reglas |
| Sin `HEALTHCHECK` en el Dockerfile (`CKV_DOCKER_2`) | Checkov | **Aceptado** como EX-001, vence el 2027-03-31 |
| 8 CVE Medium en paquetes del sistema de la imagen base | Trivy | Bajo el umbral (no High/Critical); seguidos en code scanning; Dependabot propone actualizar la imagen base |
| gitleaks revisaba todas las ramas, así que la clave de demo de una rama hacía fallar PR ajenos | diseño del pipeline | **Corregido** (PR #9): solo se revisa el historial del commit evaluado |
| CodeQL, Semgrep, auditoría NuGet, gitleaks, actionlint y zizmor sobre el código real | — | 0 hallazgos |

## 7. Lecciones

- **Las capas atrapan lo que una sola herramienta no ve.** Las reglas C# de Semgrep no marcaron el SQL
  concatenado y sus reglas de secretos no marcaron la clave genérica; el analizador de EF Core, CodeQL, ZAP y
  gitleaks sí. Ninguna herramienta sola habría detenido las seis demos.
- **Los controles baratos van primero.** Dos demos nunca llegaron a los controles costosos: una prueba y un
  analizador del compilador las detuvieron en los primeros 30 segundos.
- **La protección de push es el primer control.** GitHub rechazó la clave estilo AWS antes de que corriera CI.
- **Los escáneres necesitan alcance.** Un escaneo de secretos del historial completo que traía todas las ramas
  permitía que la fuga de una rama hiciera fallar los PR de todos; lo encontró una demo y se corrigió revisando
  solo el commit evaluado.
- **Los workflows son código y hay que analizarlos.** Dos puntos en el nombre de un paso sin comillas y una
  entrada de Checkov mal formateada rompieron CI por el camino; el control de configuración (actionlint,
  zizmor, Checkov) ahora revisa cada cambio de workflow.
- **Fijar versiones, y luego revisar lo fijado.** La última versión de la acción de Checkov seguía usando
  Checkov 2.0.930; usar la imagen oficial fijada por digest dio la versión actual.

## 8. Cómo reproducirlo

1. Hacer fork del repositorio y activar Actions.
2. Aplicar el ruleset: `gh api --method POST repos/<usuario>/<repo>/rulesets --input .github/rulesets/main.json`.
3. Abrir un pull request: corren los siete controles de CI y CodeQL. Al integrarlo, corre la publicación.
4. Verificar la imagen con los comandos de la sección 5 (cambiando el dueño y el repositorio).
5. En local: SDK de .NET 10, `dotnet test`; resultados con `python tools/collect_results.py <dueño/repo>`.

## 9. Correspondencia con marcos

| Control | Marco | Dónde |
|---|---|---|
| PW.7 Revisar y analizar el código / PW.8 Probar el ejecutable | NIST SSDF | sast, CodeQL, build-test, dast |
| PS.2 Verificar la integridad de las versiones / PS.3 Archivar y proteger cada versión | NIST SSDF | firma cosign, procedencia, SBOM en GHCR |
| PO.3 Cadena de herramientas segura | NIST SSDF | acciones e imágenes fijadas, workflows con mínimo privilegio, zizmor |
| Verificación: pruebas de seguridad; Implementación: compilación segura | OWASP SAMM | controles, registro de excepciones |
| 16.4 / 16.12 Componentes de terceros, revisiones de seguridad del código | CIS Controls v8 | auditoría NuGet, Dependabot, SAST |
| Build L2 (procedencia firmada de compilación alojada) | SLSA v1.0 | `release.yml` |
