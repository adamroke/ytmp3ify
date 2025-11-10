ytmp3ify

---

Lightweight .NET 8 microservice for modular media pipeline orchestration



ytmp3ify is a demonstration of how to build a secure, modular file-processing service using ASP.NET Core MVC and minimal APIs. It manages authentication, job scheduling, and pipeline execution for external media-processing tools.



The project emphasizes:

&nbsp;   ✅ Clean separation of concerns between API, service, and worker layers

&nbsp;   ✅ Asynchronous job orchestration with cancellation support

&nbsp;   ✅ Integration with third-party CLI tools via sandboxed processes

&nbsp;   ✅ Secure cookie-based authentication and endpoint protection

&nbsp;   ✅ Streamlined file streaming and caching mechanisms



⚙️ Architecture Overview

---

Backend: ASP.NET Core 8 MVC + Minimal Endpoints

Integration Layer: Plugin-style command runner for media pipelines (e.g., YouTube-DL-Sharp)

Storage: Local sandbox with auto-cleanup

Auth: Cookie-based session layer with role isolation



🚫 Legal \& Ethical Notice

---

This project is provided for educational and research purposes only.

It does not include any copyrighted media, proprietary binaries, or direct download mechanisms.



Users are responsible for ensuring that any tools or integrations used with this code comply with all applicable copyright laws, terms of service, and fair-use guidelines.



📘 License

---

Released under the MIT License.

See LICENSE for details.

