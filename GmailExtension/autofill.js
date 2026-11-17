const profilePrefixes = ["MailManager-Gmail/2:", "MailManager-Gmail/1:"];

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
    const prefix = profilePrefixes.find(value => text.startsWith(value));
    if (text.length > 4096 || !prefix) {
        throw new Error("Invalid Mail Manager profile.");
    }

    const { firstName, lastName, login, password, birthDate, gender, phoneNumber } = JSON.parse(text.slice(prefix.length));
    if (typeof firstName !== "string" || firstName.length > 100 ||
        typeof lastName !== "string" || lastName.length > 100 ||
        typeof login !== "string" || !/^[a-z0-9][a-z0-9.]{4,28}[a-z0-9]$/u.test(login) || login.includes("..") ||
        typeof password !== "string" || password.length < 8 || password.length > 128 ||
        /[^\x20-\x7e]/u.test(password) || password.startsWith(" ") || password.endsWith(" ")) {
        throw new Error("Invalid Mail Manager profile.");
    }

    const profile = { firstName, lastName, login, password };
    if (prefix === profilePrefixes[1]) {
        return profile;
    }

    if (birthDate != null) {
        if (typeof birthDate !== "string" || !/^\d{4}-\d{2}-\d{2}$/u.test(birthDate)) {
            throw new Error("Invalid profile birthday.");
        }
        const date = new Date(birthDate + "T00:00:00Z");
        const today = new Date();
        if (!Number.isFinite(date.getTime()) || date.toISOString().slice(0, 10) !== birthDate ||
            date.getUTCFullYear() < 1900 || date.getTime() > Date.UTC(today.getFullYear(), today.getMonth(), today.getDate())) {
            throw new Error("Invalid profile birthday.");
        }
        profile.birthday = { day: date.getUTCDate(), month: date.getUTCMonth() + 1, year: date.getUTCFullYear() };
    }

    if (!["", "female", "male", "undisclosed"].includes(gender) ||
        typeof phoneNumber !== "string" || (phoneNumber !== "" && !/^\+[1-9][0-9]{6,14}$/u.test(phoneNumber))) {
        throw new Error("Invalid profile gender or phone number.");
    }
    return { ...profile, gender, phoneNumber };
}

// Chrome выполняет эту функцию в изолированном контексте страницы без доступа к модулю.
export async function fillSignupFields(expectedUrl, profile) {
    const stillOnStep = () => location.href === expectedUrl && location.origin === "https://accounts.google.com";
    if (!stillOnStep()) {
        return 0;
    }

    const fields = [
        ['input[name="firstName"], input[id="firstName"], input[autocomplete="given-name"]', ["text"], profile.firstName],
        ['input[name="lastName"], input[id="lastName"], input[autocomplete="family-name"]', ["text"], profile.lastName],
        ['input[name="day"], input[id="day"], input[autocomplete="bday-day"]', ["text", "number"], profile.birthday?.day],
        ['input[name="year"], input[id="year"], input[autocomplete="bday-year"]', ["text", "number"], profile.birthday?.year],
        ['input[name="Username"], input[name="username"], input[id="username"], input[autocomplete="username"]', ["text", "email"], profile.login],
        ['input[name="Passwd"], input[name="PasswdAgain"], input[name="ConfirmPasswd"], input[name="password"], input[name="confirmPassword"], input[autocomplete="new-password"]', ["password", "text"], profile.password],
        ['input[type="tel"][name="phoneNumber"], input[type="tel"][name="PhoneNumber"], input[type="tel"][id="phoneNumber"], input[type="tel"][id="phoneNumberId"], input[type="tel"][autocomplete="tel"]', ["tel"], profile.phoneNumber]
    ];

    const usable = field => !field.matches(':disabled, [aria-disabled="true"]') &&
        field.checkVisibility({ visibilityProperty: true, opacityProperty: true });
    const notify = field => {
        field.dispatchEvent(new Event("input", { bubbles: true }));
        field.dispatchEvent(new Event("change", { bubbles: true }));
    };
    const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value").set;
    let filled = 0;
    for (const [selector, types, value] of fields) {
        if (!value) {
            continue;
        }

        for (const field of document.querySelectorAll(selector)) {
            if (!stillOnStep() || !types.includes(field.type) || !usable(field) || field.readOnly || field.value ||
                field.autocomplete === "one-time-code") {
                continue;
            }

            setter.call(field, String(value));
            notify(field);
            filled++;
        }
    }

    const combos = [
        ["month", "bday-month", profile.birthday?.month],
        ["gender", "sex", { female: 1, male: 2, undisclosed: 3 }[profile.gender]]
    ];
    for (const [key, autocomplete, value] of combos) {
        if (!value || !stillOnStep()) {
            continue;
        }
        const native = [...document.querySelectorAll(
            `select[name="${key}"], select[id="${key}"], select[autocomplete="${autocomplete}"]`
        )].find(usable);
        if (native) {
            const option = [...native.options].find(candidate => !candidate.matches(":disabled") && candidate.value === String(value));
            if ((!native.value || native.selectedOptions[0]?.matches(":disabled")) && option) {
                Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, "value").set.call(native, option.value);
                notify(native);
                filled++;
            }
            continue;
        }

        const combo = [...document.querySelectorAll(
            `[role="combobox"][id="${key}"], [role="combobox"][name="${key}"], [role="combobox"][aria-controls="${key}"], [role="combobox"][aria-labelledby~="${key}"], [role="combobox"][aria-labelledby~="${key}-label"]`
        )].find(usable);
        if (!combo || combo.value || combo.getAttribute("data-value") ||
            combo.getAttribute("aria-expanded") !== "false") {
            continue;
        }

        // Search only the menu owned by this control, never another open dropdown.
        combo.click();
        await new Promise(resolve => setTimeout(resolve, 100));
        if (!stillOnStep()) {
            return filled;
        }
        const menuId = combo.getAttribute("aria-controls") || combo.getAttribute("aria-owns");
        const menu = (menuId && document.getElementById(menuId)) || combo.querySelector('[role="listbox"]');
        if (!menu) {
            continue;
        }
        const options = [...menu.querySelectorAll('[role="option"]')];
        const option = options.find(item => usable(item) && item.getAttribute("data-value") === String(value));
        if (option && !options.some(item => item.getAttribute("aria-selected") === "true" &&
            item.getAttribute("data-value"))) {
            option.click();
            filled++;
        } else if (combo.getAttribute("aria-expanded") === "true") {
            combo.click();
        }
    }
    return filled;
}
