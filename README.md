# Lab 04 — Secure CI/CD pipeline for a .NET API

A small ASP.NET Core ticketing API and a GitHub Actions pipeline that checks every pull request (build and
tests, dependencies, SAST, secrets, configuration, container image, DAST) and releases `main` as a signed
container image with SBOMs and build provenance.

> **Work in progress:** the API and pipeline are being built. Results will come only from real pipeline runs.

License: MIT.
