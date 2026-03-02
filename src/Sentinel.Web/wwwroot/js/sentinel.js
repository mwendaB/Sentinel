window.sentinelDownload = (fileName, content) => {
    const blob = new Blob([content], { type: "text/csv;charset=utf-8;" });
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    URL.revokeObjectURL(url);
};

window.sentinelTheme = {
    get: () => localStorage.getItem("sentinel-theme") || "system",
    apply: (mode) => {
        const root = document.documentElement;
        root.classList.remove("theme-dark", "theme-light");
        if (mode === "dark") {
            root.classList.add("theme-dark");
        } else if (mode === "light") {
            root.classList.add("theme-light");
        }
        localStorage.setItem("sentinel-theme", mode);
    },
    toggle: () => {
        const current = window.sentinelTheme.get();
        if (current === "dark") {
            window.sentinelTheme.apply("light");
            return "light";
        }
        window.sentinelTheme.apply("dark");
        return "dark";
    },
    init: () => {
        const mode = window.sentinelTheme.get();
        window.sentinelTheme.apply(mode);
    }
};

window.addEventListener("DOMContentLoaded", () => {
    if (window.sentinelTheme) {
        window.sentinelTheme.init();
    }
});
