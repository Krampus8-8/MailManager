import assert from "node:assert/strict";
import test from "node:test";
import { fillSignupFields, isSignupPage, readProfile } from "../GmailExtension/autofill.js";

const firstStep = "https://accounts.google.com/lifecycle/steps/signup/name";
const profile = { firstName: "Анна", lastName: "Морозова", login: "anna1234", password: "Example1!" };

test("registration is scoped to the official signup path", () => {
    assert.equal(isSignupPage(firstStep), true);
    assert.equal(isSignupPage("https://accounts.google.com/signup/v2/webcreateaccount"), true);
    assert.equal(isSignupPage("https://accounts.google.com/v3/signin/identifier"), false);
    assert.equal(isSignupPage("https://accounts.google.com.example/signup"), false);
    assert.equal(isSignupPage("http://accounts.google.com/signup"), false);
});

test("clipboard payload round trips Unicode names and rejects unrelated text", () => {
    assert.deepEqual(readProfile("MailManager-Gmail/1:" + JSON.stringify(profile)), profile);
    assert.throws(() => readProfile(JSON.stringify(profile)));
    assert.throws(() => readProfile("MailManager-Gmail/1:" + JSON.stringify({ ...profile, password: "no spaces " })));
});

test("filling preserves manual input and omits verification fields", () => {
    class Input {
        #value;
        constructor(name, type = "text", value = "", visible = true) {
            Object.assign(this, { name, type, visible, events: [] });
            this.#value = value;
        }
        get value() { return this.#value; }
        set value(value) { this.#value = value; }
        matches() { return false; }
        checkVisibility() { return this.visible; }
        dispatchEvent(event) { this.events.push(event.type); }
    }

    const firstName = new Input("firstName");
    const editedName = new Input("lastName", "text", "Введённая фамилия");
    const username = new Input("Username");
    const password = new Input("Passwd", "password");
    const confirmation = new Input("PasswdAgain", "password");
    const hidden = new Input("password", "password", "", false);
    const phone = new Input("phoneNumber", "tel");
    const code = new Input("code");
    const elements = [firstName, editedName, username, password, confirmation, hidden, phone, code];

    globalThis.HTMLInputElement = Input;
    globalThis.location = new URL(firstStep);
    globalThis.document = {
        querySelectorAll: selector => elements.filter(input => selector.includes('input[name="' + input.name + '"]'))
    };

    try {
        assert.equal(fillSignupFields("https://accounts.google.com/signup", profile), 0);
        assert.equal(fillSignupFields(firstStep, profile), 4);
        assert.equal(firstName.value, profile.firstName);
        assert.equal(editedName.value, "Введённая фамилия");
        assert.equal(username.value, profile.login);
        assert.equal(password.value, profile.password);
        assert.equal(confirmation.value, profile.password);
        assert.equal(hidden.value, "");
        assert.equal(phone.value, "");
        assert.equal(code.value, "");
        assert.deepEqual(password.events, ["input", "change"]);
        assert.equal(fillSignupFields(firstStep, profile), 0);
    } finally {
        delete globalThis.HTMLInputElement;
        delete globalThis.location;
        delete globalThis.document;
    }
});
