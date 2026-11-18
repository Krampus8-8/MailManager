(() => {
    const url = new URL(location.href);
    if (url.searchParams.get("service") !== "mail") {
        try {
            if (new URL(url.searchParams.get("continue")).origin !== "https://mail.google.com") return;
        } catch {
            return;
        }
    }

    const create = /^(создать аккаунт|create account)$/iu;
    const personal = /^(для личного использования|для себя|for my personal use|for myself|personal use)$/iu;
    const label = item => (item.getAttribute("aria-label") || item.textContent).trim();

    document.addEventListener("click", event => {
        const button = event.target.closest('button, [role="button"]');
        if (!button || !create.test(label(button))) return;

        const choosePersonal = () => {
            const choices = [...document.querySelectorAll('[role="menuitem"], [role="option"], [role="menu"] a, [role="menu"] button')]
                .filter(item => personal.test(label(item)) && !item.matches(':disabled, [aria-disabled="true"]') && item.checkVisibility());
            if (choices.length !== 1) return;
            observer.disconnect();
            clearTimeout(timeout);
            choices[0].click();
        };
        const observer = new MutationObserver(choosePersonal);
        const timeout = setTimeout(() => observer.disconnect(), 4000);
        observer.observe(document.body, { childList: true, subtree: true, attributes: true });
        queueMicrotask(choosePersonal);
    }, true);
})();
