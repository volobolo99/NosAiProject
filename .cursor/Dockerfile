# Cloud Agent base image for NosAiProject.
#
# The repository ships two products that must both be buildable and testable:
#   * the dependency-free Python runtime + operator dashboard (nosai/*), and
#   * the .NET 8 C# runtime (src/NosAi.Runtime), whose xUnit suite CI runs on
#     Ubuntu even though the projects target net8.0-windows (EnableWindowsTargeting).
#
# Only stable system toolchains live here. Repository dependencies tied to the
# checked-out source (pip editable install, NuGet restore) are refreshed by the
# environment install command so they follow branches and lockfiles.
FROM ubuntu:24.04

ENV DEBIAN_FRONTEND=noninteractive

# git and curl are required in fully custom images; dotnet-sdk-8.0 and the Python
# toolchain back the two products. Ubuntu 24.04 ships all of these in its archive.
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates \
        curl \
        git \
        python3 \
        python3-venv \
        python3-pip \
        dotnet-sdk-8.0 \
    && rm -rf /var/lib/apt/lists/*

# A dedicated virtualenv on PATH: Ubuntu's system interpreter is PEP 668
# externally-managed, so `pip install` into it is refused. Owned by the ubuntu
# user so the install command's editable install can write into it.
ENV VIRTUAL_ENV=/opt/venv
RUN python3 -m venv "$VIRTUAL_ENV" \
    && "$VIRTUAL_ENV/bin/pip" install --no-cache-dir --upgrade pip \
    && chown -R ubuntu:ubuntu "$VIRTUAL_ENV"
ENV PATH="$VIRTUAL_ENV/bin:$PATH"

# Quiet, non-interactive dotnet CLI for reproducible, log-friendly setup.
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

USER ubuntu
