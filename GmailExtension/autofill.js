const profilePrefix = "MailManager-Gmail/1:";

export function isSignupPage(value) {
    try {
        const url = new URL(value);
        return url.origin === "https://accounts.google.com"
            && /^\/(?:signup|lifecycle\/steps\/signup|v[23]\/signup)(?:\/|$)/u.test(url.pathname);
    } catch {
        return false;
    }
}

export function readProfile(text) {
    if (text.length > 4096 || !text.startsWith(profilePrefix)) {
        throw new Error("Invalid Mail Manager profile.");
    }

    const { firstName, lastName, login, password } = JSON.parse(text.slice(profilePrefix.length));
    if (typeof firstName !== "string" || firstName.length > 100 ||
        typeof lastName !== "string" || lastName.length > 100 ||
        typeof login !== "string" || !/^[a-z0-9][a-z0-9.]{4,28}[a-z0-9]$/u.test(login) || login.includes("..") ||
        typeof password !== "string" || password.length < 8 || password.length > 128 ||
        /[^\x20-\x7e]/u.test(password) || password.startsWith(" ") || password.endsWith(" ")) {
        throw new Error("Invalid Mail Manager profile.");
    }

    return { firstName, lastName, login, password };
}

// Chrome выполняет эту функцию в изолированном контексте страницы без доступа к модулю.
export function fillSignupFields(expectedUrl, profile) {
    if (location.href !== expectedUrl || location.origin !== "https://accounts.google.com") {
        return 0;
    }

    const fields = [
        ['input[name="firstName"], input[id="firstName"], input[autocomplete="given-name"]', ["text"], profile.firstName],
        ['input[name="lastName"], input[id="lastName"], input[autocomplete="family-name"]', ["text"], profile.lastName],
        ['input[name="Username"], input[name="username"], input[id="username"], input[autocomplete="username"]', ["text", "email"], profile.login],
        ['input[name="Passwd"], input[name="PasswdAgain"], input[name="ConfirmPasswd"], input[name="password"], input[name="confirmPassword"], input[autocomplete="new-password"]', ["password", "text"], profile.password]
    ];

    const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value").set;
    let filled = 0;
    for (const [selector, types, value] of fields) {
        if (!value) {
            continue;
        }

        for (const field of document.querySelectorAll(selector)) {
            if (!types.includes(field.type) || field.matches(":disabled") || field.readOnly || field.value ||
                !field.checkVisibility({ visibilityProperty: true, opacityProperty: true })) {
                continue;
            }

            setter.call(field, value);
            field.dispatchEvent(new Event("input", { bubbles: true }));
            field.dispatchEvent(new Event("change", { bubbles: true }));
            filled++;
        }
    }

    return filled;
}
