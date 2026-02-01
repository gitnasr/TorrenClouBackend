# Contributing to TorreClou

Thank you for your interest in contributing to TorreClou! We welcome contributions from the community.

## Table of Contents

- [Code of Conduct](#code-of-conduct)
- [Getting Started](#getting-started)
- [Development Setup](#development-setup)
- [Making Changes](#making-changes)
- [Coding Standards](#coding-standards)
- [Commit Guidelines](#commit-guidelines)
- [Pull Request Process](#pull-request-process)
- [Reporting Bugs](#reporting-bugs)
- [Feature Requests](#feature-requests)

## Code of Conduct

This project follows a Code of Conduct that all contributors are expected to uphold. Please be respectful and constructive in all interactions.

## Getting Started

1. Fork the repository
2. Clone your fork: `git clone https://github.com/yourusername/torrenclo.git`
3. Create a feature branch: `git checkout -b feature/your-feature-name`
4. Make your changes
5. Test your changes
6. Submit a pull request

## Development Setup

### Prerequisites

- **.NET 9.0 SDK** - [Download](https://dotnet.microsoft.com/download/dotnet/9.0)
- **Docker & Docker Compose** - [Download](https://docs.docker.com/get-docker/)
- **PostgreSQL 17** (or use Docker)
- **Redis 7** (or use Docker)
- **Git**
- **IDE**: Visual Studio 2022, Rider, or VS Code

### Local Development

1. **Start infrastructure services**
   ```bash
   docker-compose up -d postgres redis loki prometheus grafana
   ```

2. **Configure environment**
   ```bash
   cp .env.example .env
   # Edit .env with your local settings
   ```

3. **Apply database migrations**
   ```bash
   dotnet ef database update --project TorreClou.Infrastructure --startup-project TorreClou.API
   ```

4. **Run the API**
   ```bash
   dotnet run --project TorreClou.API
   ```

5. **Run workers** (in separate terminals)
   ```bash
   dotnet run --project TorreClou.Worker
   dotnet run --project TorreClou.GoogleDrive.Worker
   dotnet run --project TorreClou.S3.Worker
   ```

### Running Tests

```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=opencover
```

### Building Docker Images

```bash
# Build all images
docker-compose build

# Build specific service
docker-compose build api
```

## Making Changes

### Branch Naming

- `feature/` - New features (e.g., `feature/magnet-link-support`)
- `fix/` - Bug fixes (e.g., `fix/upload-timeout-error`)
- `docs/` - Documentation updates (e.g., `docs/setup-guide`)
- `refactor/` - Code refactoring (e.g., `refactor/torrent-service`)
- `test/` - Test additions/fixes (e.g., `test/analysis-service`)

### Project Structure

```
TorreClou.Core/          # Domain layer (entities, DTOs, interfaces)
TorreClou.Application/   # Application layer (business logic, services)
TorreClou.Infrastructure/ # Infrastructure layer (database, external services)
TorreClou.API/           # Presentation layer (controllers, middleware)
TorreClou.Worker/        # Background workers (torrent downloads)
TorreClou.GoogleDrive.Worker/ # Google Drive upload worker
TorreClou.S3.Worker/     # AWS S3 upload worker
```

## Coding Standards

### C# Style Guide

- Follow [Microsoft C# Coding Conventions](https://docs.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- Use meaningful variable and method names
- Keep methods small and focused (Single Responsibility Principle)
- Use async/await for I/O operations
- Add XML documentation comments to public APIs
- Use `var` when the type is obvious
- Prefer `readonly` fields when possible
- Use pattern matching where appropriate

### Code Examples

**Good:**
```csharp
public async Task<Result<TorrentDto>> AnalyzeTorrentAsync(
    AnalyzeTorrentRequestDto request,
    int userId,
    Stream torrentFile)
{
    var metadata = await _torrentParser.ParseAsync(torrentFile);

    if (metadata.TotalSize > MaxTorrentSize)
        return Result<TorrentDto>.Failure("Torrent exceeds maximum size");

    return Result<TorrentDto>.Success(metadata);
}
```

**Avoid:**
```csharp
public async Task<object> DoStuff(object x, object y)
{
    var z = await SomeMethod(x);
    // Long method with unclear purpose
    return z;
}
```

### Architecture Principles

- **Clean Architecture** - Keep dependencies pointing inward
- **SOLID Principles** - Especially Single Responsibility and Dependency Inversion
- **Repository Pattern** - For data access abstraction
- **Result Pattern** - For error handling without exceptions
- **Dependency Injection** - Use constructor injection

### Testing

- Write unit tests for business logic
- Use integration tests for database operations
- Mock external dependencies (Redis, cloud storage)
- Aim for >70% code coverage on critical paths

## Commit Guidelines

### Commit Message Format

```
<type>(<scope>): <subject>

<body>

<footer>
```

**Types:**
- `feat` - New feature
- `fix` - Bug fix
- `docs` - Documentation changes
- `style` - Code style changes (formatting, no logic change)
- `refactor` - Code refactoring
- `test` - Adding or updating tests
- `chore` - Maintenance tasks (dependencies, config)

**Examples:**
```
feat(api): add magnet link support for torrent analysis

Implement MagnetLinkParser to handle magnet URIs alongside .torrent files.
Adds new endpoint POST /api/torrents/analyze-magnet.

Closes #42
```

```
fix(worker): resolve timeout on large torrent downloads

Increased qBittorrent connection timeout from 30s to 120s to handle
slow trackers. Added retry logic for transient failures.

Fixes #87
```

### Atomic Commits

- Make small, focused commits
- Each commit should represent one logical change
- Commit messages should explain **why**, not just **what**

## Pull Request Process

1. **Update your fork**
   ```bash
   git checkout main
   git pull upstream main
   git checkout your-feature-branch
   git rebase main
   ```

2. **Ensure tests pass**
   ```bash
   dotnet build
   dotnet test
   ```

3. **Update documentation** if needed
   - README.md
   - API documentation
   - Code comments

4. **Create Pull Request**
   - Provide clear title and description
   - Reference related issues (Fixes #123, Closes #456)
   - Add screenshots/videos for UI changes
   - Mark as draft if work in progress

5. **Code Review**
   - Address reviewer feedback
   - Keep discussions professional and constructive
   - Update code based on suggestions

6. **Merge**
   - Squash commits if requested
   - Maintainers will merge when approved

### PR Checklist

- [ ] Code builds without errors
- [ ] All tests pass
- [ ] New tests added for new functionality
- [ ] Documentation updated
- [ ] No hardcoded credentials or secrets
- [ ] Code follows style guide
- [ ] Commit messages follow guidelines
- [ ] PR description is clear and complete

## Reporting Bugs

When reporting bugs, please include:

1. **Description** - Clear summary of the issue
2. **Steps to Reproduce** - Detailed steps to trigger the bug
3. **Expected Behavior** - What should happen
4. **Actual Behavior** - What actually happens
5. **Environment**:
   - OS (Windows, Linux, macOS)
   - .NET version
   - Docker version
   - Browser (if API related)
6. **Logs** - Relevant error messages or stack traces
7. **Screenshots** - If applicable

Use the bug report template when creating an issue.

## Feature Requests

We welcome feature requests! Please:

1. Check if the feature already exists or is planned
2. Describe the problem you're trying to solve
3. Explain your proposed solution
4. Consider alternative approaches
5. Discuss potential impacts on existing functionality

Use the feature request template when creating an issue.

## Questions?

- Open a [Discussion](https://github.com/yourusername/torrenclo/discussions)
- Ask in [Issues](https://github.com/yourusername/torrenclo/issues) with the question label
- Check existing documentation in `/docs`

## License

By contributing, you agree that your contributions will be licensed under the MIT License.

---

Thank you for contributing to TorreClou! 🎉
