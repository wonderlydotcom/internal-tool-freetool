(() => {
  function closest(target, selector) {
    return target instanceof Element ? target.closest(selector) : null;
  }

  function updatePermissionsSaveState(container) {
    const saveButton = container.querySelector("[data-permissions-save]");
    if (!(saveButton instanceof HTMLButtonElement)) return;

    const checkboxes = Array.from(
      container.querySelectorAll("[data-permission-checkbox]")
    );

    const hasChanges = checkboxes.some((checkbox) => {
      if (!(checkbox instanceof HTMLInputElement)) return false;
      const initialValue = checkbox.getAttribute("data-initial-checked") === "true";
      return checkbox.checked !== initialValue;
    });

    saveButton.disabled = !hasChanges;
  }

  function initializePermissionsMatrices() {
    document
      .querySelectorAll("[data-permissions-matrix]")
      .forEach(updatePermissionsSaveState);
  }

  function updateTypedConfirmState(form) {
    const input = form.querySelector("[data-confirm-input]");
    const submitButton = form.querySelector("[data-confirm-submit]");
    if (!(input instanceof HTMLInputElement)) return;
    if (!(submitButton instanceof HTMLButtonElement)) return;

    const expected = input.getAttribute("data-confirm-expected") || "";
    submitButton.disabled = input.value.trim() !== expected;
  }

  function initializeTypedConfirmForms() {
    document
      .querySelectorAll("[data-typed-confirm-form]")
      .forEach(updateTypedConfirmState);
  }

  function updateAddMemberSubmitState(form) {
    const select = form.querySelector("[data-add-member-select]");
    const submitButton = form.querySelector("[data-add-member-submit]");
    if (!(select instanceof HTMLSelectElement)) return;
    if (!(submitButton instanceof HTMLButtonElement)) return;

    submitButton.disabled = select.disabled || select.value === "";
  }

  function initializeAddMemberForms(root = document) {
    const forms = [];
    if (root instanceof Element && root.matches("[data-add-member-form]")) {
      forms.push(root);
    }
    root.querySelectorAll?.("[data-add-member-form]").forEach((form) => forms.push(form));

    forms.forEach(updateAddMemberSubmitState);
  }

  function editableNameInput(form) {
    const input = form.querySelector("[data-editable-name-input]");
    return input instanceof HTMLInputElement ? input : null;
  }

  function clearEditableNameError(form) {
    const error = form.querySelector("[data-editable-name-error]");
    if (!(error instanceof HTMLElement)) return;

    error.textContent = "";
    error.hidden = true;
  }

  function setEditableNameEditing(form, editing, shouldFocus = false) {
    const input = editableNameInput(form);
    if (!input || input.disabled) return;

    input.readOnly = !editing;
    input.toggleAttribute("readonly", !editing);
    input.setAttribute("aria-readonly", editing ? "false" : "true");
    form.classList.toggle("is-editing", editing);

    if (editing && shouldFocus) {
      window.setTimeout(() => {
        input.focus();
        input.setSelectionRange(input.value.length, input.value.length);
      }, 0);
    }
  }

  function submitEditableNameForm(form) {
    const input = editableNameInput(form);
    if (!input || input.readOnly || form.dataset.saving === "true") return;

    const initialValue = input.getAttribute("data-initial-value") || input.defaultValue || "";
    const currentValue = input.value.trim();

    if (currentValue === initialValue.trim()) {
      input.value = initialValue;
      clearEditableNameError(form);
      setEditableNameEditing(form, false);
      return;
    }

    if (!input.checkValidity()) {
      input.reportValidity();
      return;
    }

    form.dataset.saving = "true";
    form.classList.add("is-saving");
    setEditableNameEditing(form, false);
    if (typeof form.requestSubmit === "function") form.requestSubmit();
    else form.submit();
  }

  function initializeEditableNameForms(root = document) {
    const forms = [];
    if (root instanceof Element && root.matches("[data-editable-name-form]")) {
      forms.push(root);
    }
    root.querySelectorAll?.("[data-editable-name-form]").forEach((form) => forms.push(form));

    forms.forEach((form) => {
      const input = editableNameInput(form);
      if (!input || input.getAttribute("data-editable-name-autofocus") !== "true") return;

      input.removeAttribute("data-editable-name-autofocus");
      setEditableNameEditing(form, true, true);
    });
  }

  function requestSubmitOnChange(control) {
    const form = control.closest("form");
    if (!(form instanceof HTMLFormElement) || form.dataset.saving === "true") return;

    const initialValue = control.getAttribute("data-initial-value");
    if (initialValue !== null && control.value === initialValue) return;

    form.dataset.saving = "true";
    if (typeof form.requestSubmit === "function") form.requestSubmit();
    else form.submit();
  }

  function resetFormControls(container) {
    container.querySelectorAll("input, textarea, select").forEach((control) => {
      if (control instanceof HTMLInputElement) {
        if (control.type === "checkbox" || control.type === "radio") {
          control.checked = control.defaultChecked;
        } else {
          control.value = control.defaultValue || "";
        }
      } else if (control instanceof HTMLTextAreaElement) {
        control.value = control.defaultValue || "";
      } else if (control instanceof HTMLSelectElement) {
        control.selectedIndex = 0;
      }
    });
  }

  const currentUserSuggestions = [
    { title: "current_user.email", group: "User Context", meta: "string" },
    { title: "current_user.id", group: "User Context", meta: "string" },
    { title: "current_user.firstName", group: "User Context", meta: "string" },
    { title: "current_user.lastName", group: "User Context", meta: "string" },
  ];

  const templateMirrors = new WeakMap();
  let templatePopover;
  let activeTemplateInput = null;
  let activeTemplateTrigger = null;
  let activeTemplateSuggestions = [];
  let activeTemplateSuggestionIndex = 0;
  let activeExpressionControl = null;
  let activeExpressionRange = null;
  let activeExpressionMode = "insert";
  let lastExpressionBraceControl = null;
  let lastExpressionBraceTime = 0;

  function escapeHtml(value) {
    return value.replace(/[&<>"']/g, (char) => {
      switch (char) {
        case "&":
          return "&amp;";
        case "<":
          return "&lt;";
        case ">":
          return "&gt;";
        case '"':
          return "&quot;";
        case "'":
          return "&#39;";
        default:
          return char;
      }
    });
  }

  function isTemplateControl(element) {
    return (
      (element instanceof HTMLInputElement ||
        element instanceof HTMLTextAreaElement) &&
      element.matches("[data-template-input]")
    );
  }

  function templateContext(control) {
    if (control?.matches("[data-expression-editor-input]") && activeExpressionControl) {
      return templateContext(activeExpressionControl);
    }
    return control.closest("[data-app-config-form]") || document;
  }

  function appInputSuggestions(control) {
    const context = templateContext(control);
    const seen = new Set();
    const suggestions = [];

    context
      .querySelectorAll(".input-row:not(.input-row-template)")
      .forEach((row) => {
        const titleInput = row.querySelector('input[name="InputTitle"]');
        if (!(titleInput instanceof HTMLInputElement)) return;

        const title = titleInput.value.trim();
        if (!title || seen.has(title)) return;

        const requiredInput = row.querySelector("[data-input-required-value]");
        const required =
          requiredInput instanceof HTMLInputElement &&
          requiredInput.value === "true";

        seen.add(title);
        suggestions.push({
          title,
          group: "App Fields",
          meta: required ? "Required" : "",
        });
      });

    return suggestions;
  }

  function templateSuggestions(control) {
    return [...currentUserSuggestions, ...appInputSuggestions(control)];
  }

  function variableNeedsQuotes(title) {
    return !/^[A-Za-z_][A-Za-z0-9_]*$/.test(title);
  }

  function variableSyntax(title) {
    const safeTitle = title.replaceAll('"', "");
    return variableNeedsQuotes(safeTitle) ? `@"${safeTitle}"` : `@${safeTitle}`;
  }

  function currentUserTitle(title) {
    return title.startsWith("current_user.");
  }

  function createVariableRegex() {
    return /@(?:"([^"]+)"|([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)?))/g;
  }

  function extractExpressionVariables(expression) {
    const variables = [];
    for (const match of expression.matchAll(createVariableRegex())) {
      const name = match[1] ?? match[2];
      if (name && !variables.includes(name)) variables.push(name);
    }
    return variables;
  }

  function isJsonExpression(expression) {
    const trimmed = expression.trim();
    return trimmed.startsWith("{") || trimmed.startsWith("[");
  }

  function isIndexInJsonString(expression, index) {
    let inString = false;
    for (let i = 0; i < index; i += 1) {
      if (expression[i] !== '"') continue;

      let backslashes = 0;
      for (let j = i - 1; j >= 0 && expression[j] === "\\"; j -= 1) {
        backslashes += 1;
      }
      if (backslashes % 2 === 0) inString = !inString;
    }
    return inString;
  }

  function replaceVariablesForJsonValidation(expression) {
    let error = "";
    const result = expression.replace(
      createVariableRegex(),
      (match, quotedName, unquotedName, offset) => {
        if (error) return match;
        if (isIndexInJsonString(expression, offset)) {
          error = "Variables cannot be used inside JSON strings; use @Var as a JSON value.";
          return match;
        }
        const name = quotedName ?? unquotedName;
        return name ? "0" : match;
      }
    );
    return { result, error };
  }

  function normalizeExpressionForSyntax(expression) {
    let variableIndex = 0;
    return expression.replace(createVariableRegex(), (_match, quotedName, unquotedName) => {
      const name = quotedName ?? unquotedName;
      if (!name) return _match;
      variableIndex += 1;
      return `__var${variableIndex}`;
    });
  }

  function validateExpression(expression, control) {
    const trimmed = expression.trim();
    const errors = [];
    const referencedVariables = extractExpressionVariables(trimmed);
    const validTitles = new Set(templateSuggestions(control).map((item) => item.title));

    if (!trimmed) {
      return {
        isValid: false,
        errors: ["Expression cannot be empty"],
        referencedVariables,
      };
    }

    referencedVariables.forEach((name) => {
      if (currentUserTitle(name)) {
        const property = name.slice("current_user.".length);
        if (!currentUserSuggestions.some((suggestion) => suggestion.title === name)) {
          errors.push(`Unknown current_user property: ${property}`);
        }
      } else if (!validTitles.has(name)) {
        errors.push(`Unknown variable: ${name}`);
      }
    });

    if (isJsonExpression(trimmed)) {
      const { result, error } = replaceVariablesForJsonValidation(trimmed);
      if (error) {
        errors.push(error);
      } else {
        try {
          JSON.parse(result);
        } catch (parseError) {
          errors.push(`JSON syntax error: ${parseError.message || String(parseError)}`);
        }
      }
    } else {
      try {
        const normalized = normalizeExpressionForSyntax(trimmed);
        if (/(^|[^=!<>])=(?!=)/.test(normalized)) {
          throw new Error("Assignments are not supported");
        }
        // Parse only; the function is never invoked. This mirrors the old editor's
        // JavaScript-like syntax checks without making client validation authoritative.
        Function(`"use strict"; return (${normalized});`);
      } catch (parseError) {
        errors.push(`Syntax error: ${parseError.message || String(parseError)}`);
      }
    }

    return { isValid: errors.length === 0, errors, referencedVariables };
  }

  function highlightTemplateValue(value, control) {
    if (!value) return "";

    const validTitles = new Set(templateSuggestions(control).map((item) => item.title));
    const tokenPattern = /\{\{[\s\S]*?\}\}|@(?:"([^"]*)"?|([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)?|))/g;
    let html = "";
    let lastIndex = 0;

    for (const match of value.matchAll(tokenPattern)) {
      const index = match.index || 0;
      const token = match[0];
      if (index > lastIndex) {
        html += escapeHtml(value.slice(lastIndex, index));
      }

      if (token.startsWith("{{")) {
        const expression = token.slice(2, -2).trim();
        const validation = validateExpression(expression, control);
        const tokenClass = validation.isValid
          ? "template-token-expression"
          : "template-token-expression template-token-invalid";
        html += `<span class="template-token ${tokenClass}">${escapeHtml(token)}</span>`;
      } else {
        const title = match[1] ?? match[2] ?? "";
        const isValid = title && validTitles.has(title);
        const isCurrentUser = title && currentUserTitle(title);
        const tokenClass = isValid
          ? isCurrentUser
            ? "template-token-user"
            : "template-token-input"
          : title
            ? "template-token-invalid"
            : "template-token-active";

        html += `<span class="template-token ${tokenClass}">${escapeHtml(token)}</span>`;
      }

      lastIndex = index + token.length;
    }

    if (lastIndex < value.length) {
      html += escapeHtml(value.slice(lastIndex));
    }

    return html;
  }

  function updateTemplateMirror(control) {
    const mirror = templateMirrors.get(control);
    if (!mirror) return;

    mirror.innerHTML = highlightTemplateValue(control.value, control);
    mirror.scrollLeft = control.scrollLeft;
    mirror.scrollTop = control.scrollTop;
  }

  function syncTemplateMirrors(context) {
    context.querySelectorAll("[data-template-input]").forEach((control) => {
      if (isTemplateControl(control)) updateTemplateMirror(control);
    });
  }

  function initializeTemplateInput(control) {
    if (!isTemplateControl(control)) return;
    if (control.dataset.templateEnhanced === "true") return;
    if (control.closest(".kv-row-template, .input-row-template, .sql-row-template")) return;

    const wrapper = document.createElement("span");
    wrapper.className = "template-input-shell";
    if (control instanceof HTMLTextAreaElement) {
      wrapper.classList.add("template-input-shell-multiline");
    }

    const mirror = document.createElement("span");
    mirror.className = "template-input-mirror";
    mirror.setAttribute("aria-hidden", "true");

    control.parentNode.insertBefore(wrapper, control);
    wrapper.appendChild(mirror);
    wrapper.appendChild(control);
    control.classList.add("template-input-control");
    control.dataset.templateEnhanced = "true";
    control.setAttribute("autocomplete", "off");
    templateMirrors.set(control, mirror);
    updateTemplateMirror(control);
  }

  function initializeTemplateInputs(root = document) {
    if (root instanceof Element && root.matches("[data-template-input]")) {
      initializeTemplateInput(root);
    }

    root.querySelectorAll("[data-template-input]").forEach((control) => {
      initializeTemplateInput(control);
    });
  }

  function templateTrigger(control) {
    const cursor = control.selectionStart;
    if (cursor === null) return null;

    const beforeCursor = control.value.slice(0, cursor);
    const atIndex = beforeCursor.lastIndexOf("@");
    if (atIndex < 0) return null;

    const fragment = beforeCursor.slice(atIndex + 1);
    if (fragment.includes("\n")) return null;

    if (fragment.startsWith('"')) {
      const query = fragment.slice(1);
      if (query.includes('"')) return null;
      return { start: atIndex, end: cursor, query };
    }

    if (/\s/.test(fragment)) return null;
    if (!/^[A-Za-z0-9_.-]*$/.test(fragment)) return null;

    return { start: atIndex, end: cursor, query: fragment };
  }

  function templatePopoverOwner(control) {
    return control?.closest("dialog") || document.body;
  }

  function ensureTemplatePopover(owner = document.body) {
    if (!templatePopover) {
      templatePopover = document.createElement("div");
      templatePopover.className = "template-suggestions";
      templatePopover.setAttribute("data-template-suggestions", "true");
      templatePopover.hidden = true;
    }

    if (templatePopover.parentElement !== owner) owner.appendChild(templatePopover);
    return templatePopover;
  }

  function closeTemplatePopover() {
    if (templatePopover) templatePopover.hidden = true;
    activeTemplateInput = null;
    activeTemplateTrigger = null;
    activeTemplateSuggestions = [];
    activeTemplateSuggestionIndex = 0;
  }

  function positionTemplatePopover(control) {
    const popover = ensureTemplatePopover(templatePopoverOwner(control));
    const rect = control.getBoundingClientRect();
    popover.style.left = `${rect.left}px`;
    popover.style.top = `${rect.bottom + 6}px`;
    popover.style.minWidth = `${Math.min(Math.max(rect.width, 260), 420)}px`;
  }

  function renderTemplatePopover(control, trigger) {
    const popover = ensureTemplatePopover(templatePopoverOwner(control));
    const query = trigger.query.toLowerCase();
    const suggestions = templateSuggestions(control).filter((item) =>
      item.title.toLowerCase().includes(query)
    );

    activeTemplateInput = control;
    activeTemplateTrigger = trigger;
    activeTemplateSuggestions = suggestions;
    activeTemplateSuggestionIndex = Math.min(
      activeTemplateSuggestionIndex,
      Math.max(suggestions.length - 1, 0)
    );

    if (suggestions.length === 0) {
      popover.innerHTML = '<div class="template-suggestion-empty">No variables found.</div>';
      positionTemplatePopover(control);
      popover.hidden = false;
      return;
    }

    popover.replaceChildren();
    let currentGroup = "";

    suggestions.forEach((suggestion, index) => {
      if (suggestion.group !== currentGroup) {
        currentGroup = suggestion.group;
        const heading = document.createElement("div");
        heading.className = "template-suggestion-heading";
        heading.textContent = currentGroup;
        popover.appendChild(heading);
      }

      const item = document.createElement("button");
      item.type = "button";
      item.className = "template-suggestion-item";
      if (index === activeTemplateSuggestionIndex) {
        item.classList.add("is-active");
      }
      item.setAttribute("data-template-suggestion-index", String(index));
      item.innerHTML = `<span>${escapeHtml(suggestion.title)}</span>${
        suggestion.meta
          ? `<span class="template-suggestion-meta">${escapeHtml(suggestion.meta)}</span>`
          : ""
      }`;
      item.addEventListener("mousedown", (event) => {
        event.preventDefault();
        selectTemplateSuggestion(index);
      });
      popover.appendChild(item);
    });

    positionTemplatePopover(control);
    popover.hidden = false;
  }

  function maybeOpenTemplatePopover(control, resetIndex = true) {
    const trigger = templateTrigger(control);
    if (trigger) {
      if (resetIndex) activeTemplateSuggestionIndex = 0;
      renderTemplatePopover(control, trigger);
    } else {
      closeTemplatePopover();
    }
  }

  function selectTemplateSuggestion(index) {
    const control = activeTemplateInput;
    const trigger = activeTemplateTrigger;
    const suggestion = activeTemplateSuggestions[index];
    if (!control || !trigger || !suggestion) return;

    const token = variableSyntax(suggestion.title);
    control.value = `${control.value.slice(0, trigger.start)}${token}${control.value.slice(trigger.end)}`;
    const cursor = trigger.start + token.length;
    control.setSelectionRange(cursor, cursor);
    updateTemplateMirror(control);
    closeTemplatePopover();
    control.focus();
  }

  function expressionModal() {
    return document.getElementById("template-expression-modal");
  }

  function expressionEditorInput() {
    const modal = expressionModal();
    const input = modal?.querySelector("[data-expression-editor-input]");
    return input instanceof HTMLTextAreaElement ? input : null;
  }

  function expressionSaveButton() {
    const modal = expressionModal();
    const button = modal?.querySelector("[data-expression-modal-save]");
    return button instanceof HTMLButtonElement ? button : null;
  }

  function expressionValidationElement() {
    const modal = expressionModal();
    const element = modal?.querySelector("[data-expression-validation]");
    return element instanceof HTMLElement ? element : null;
  }

  function expressionRangeAtCursor(control) {
    const cursor = control.selectionStart;
    if (cursor === null) return null;

    const pattern = /\{\{[\s\S]*?\}\}/g;
    for (const match of control.value.matchAll(pattern)) {
      const start = match.index || 0;
      const end = start + match[0].length;
      if (cursor >= start && cursor <= end) {
        return {
          start,
          end,
          expression: match[0].slice(2, -2).trim(),
        };
      }
    }

    return null;
  }

  function updateExpressionModalValidation() {
    const input = expressionEditorInput();
    const validationElement = expressionValidationElement();
    const saveButton = expressionSaveButton();
    if (!input || !validationElement || !saveButton) return;

    const validation = validateExpression(input.value, activeExpressionControl || input);
    validationElement.textContent = validation.isValid
      ? "Expression is valid"
      : validation.errors.join(", ");
    validationElement.classList.toggle("is-valid", validation.isValid);
    validationElement.classList.toggle("is-invalid", !validation.isValid);
    saveButton.disabled = !validation.isValid;
    saveButton.textContent = activeExpressionMode === "edit" ? "Save" : "Insert";
    updateTemplateMirror(input);
  }

  function openExpressionModal(control, mode, range) {
    const modal = expressionModal();
    const input = expressionEditorInput();
    if (!modal || !input) return;

    activeExpressionControl = control;
    activeExpressionRange = range;
    activeExpressionMode = mode;

    const title = modal.querySelector("#template-expression-title");
    if (title) title.textContent = mode === "edit" ? "Edit Expression" : "Insert Expression";

    input.value = range?.expression || "";
    updateExpressionModalValidation();
    closeTemplatePopover();

    if (typeof modal.showModal === "function") modal.showModal();
    else modal.setAttribute("open", "open");

    window.setTimeout(() => {
      input.focus();
      input.setSelectionRange(input.value.length, input.value.length);
      updateTemplateMirror(input);
    }, 0);
  }

  function closeExpressionModal() {
    const modal = expressionModal();
    if (modal?.open && typeof modal.close === "function") modal.close();
    else modal?.removeAttribute("open");

    activeExpressionControl = null;
    activeExpressionRange = null;
    activeExpressionMode = "insert";
  }

  function saveExpressionModal() {
    const input = expressionEditorInput();
    const control = activeExpressionControl;
    if (!input || !control) return;

    const expression = input.value.trim();
    const validation = validateExpression(expression, control);
    if (!validation.isValid) {
      updateExpressionModalValidation();
      return;
    }

    const selectionStart = control.selectionStart ?? control.value.length;
    const selectionEnd = control.selectionEnd ?? selectionStart;
    const range = activeExpressionRange || {
      start: selectionStart,
      end: selectionEnd,
    };
    const token = `{{ ${expression} }}`;
    control.value = `${control.value.slice(0, range.start)}${token}${control.value.slice(range.end)}`;
    const cursor = range.start + token.length;
    control.setSelectionRange(cursor, cursor);
    updateTemplateMirror(control);
    control.dispatchEvent(new Event("input", { bubbles: true }));
    closeExpressionModal();
    control.focus();
  }

  function maybeOpenExpressionAtCursor(control) {
    if (control.matches("[data-disable-expression-modal]")) return;
    const range = expressionRangeAtCursor(control);
    if (!range) return;
    openExpressionModal(control, "edit", range);
  }

  function handleExpressionBraceShortcut(event, control) {
    if (control.matches("[data-disable-expression-modal]")) return false;
    if (event.key !== "{") {
      lastExpressionBraceControl = null;
      lastExpressionBraceTime = 0;
      return false;
    }

    const cursor = control.selectionStart;
    const selectionEnd = control.selectionEnd;
    if (cursor === null || selectionEnd === null || cursor !== selectionEnd) {
      lastExpressionBraceControl = control;
      lastExpressionBraceTime = Date.now();
      return false;
    }

    const now = Date.now();
    const previousChar = control.value.slice(cursor - 1, cursor);
    if (
      previousChar === "{" &&
      lastExpressionBraceControl === control &&
      now - lastExpressionBraceTime < 500
    ) {
      event.preventDefault();
      control.value = `${control.value.slice(0, cursor - 1)}${control.value.slice(cursor)}`;
      control.setSelectionRange(cursor - 1, cursor - 1);
      updateTemplateMirror(control);
      openExpressionModal(control, "insert", { start: cursor - 1, end: cursor - 1 });
      lastExpressionBraceControl = null;
      lastExpressionBraceTime = 0;
      return true;
    }

    lastExpressionBraceControl = control;
    lastExpressionBraceTime = now;
    return false;
  }

  function updateAppResourceSections(form) {
    const select = form.querySelector("[data-app-resource-select]");
    if (!(select instanceof HTMLSelectElement)) return;

    const selectedOption = select.options[select.selectedIndex];
    const resourceKind =
      selectedOption?.getAttribute("data-resource-kind") || "http";
    const resourceId = select.value;

    form.querySelectorAll("[data-resource-kind-section]").forEach((section) => {
      const sectionKind = section.getAttribute("data-resource-kind-section");
      section.hidden = sectionKind !== resourceKind;
    });

    form.querySelectorAll("[data-resource-preview]").forEach((preview) => {
      preview.hidden = preview.getAttribute("data-resource-preview") !== resourceId;
    });
  }

  function updateResourceFormSections(form) {
    const select = form.querySelector("[data-resource-form-kind-select]");
    const selectedKind = select instanceof HTMLSelectElement ? select.value : "http";

    form.querySelectorAll("[data-resource-form-kind-section]").forEach((section) => {
      section.hidden = section.getAttribute("data-resource-form-kind-section") !== selectedKind;
    });
  }

  function selectedSqlMode(form) {
    const checked = form.querySelector('input[name="SqlMode"]:checked');
    return checked instanceof HTMLInputElement ? checked.value : "gui";
  }

  function updateSqlModeSections(form) {
    const mode = selectedSqlMode(form);

    form.querySelectorAll("[data-sql-mode-section]").forEach((section) => {
      section.hidden = section.getAttribute("data-sql-mode-section") !== mode;
    });

    form.querySelectorAll("[data-sql-mode-control]").forEach((control) => {
      const option = control.closest(".sql-mode-option");
      if (option) option.classList.toggle("is-active", control.value === mode);
    });
  }

  function syncSqlFilterRow(row) {
    const operator = row.querySelector("[data-sql-filter-operator]");
    const value = row.querySelector("[data-sql-filter-value]");
    if (!(operator instanceof HTMLSelectElement)) return;
    if (!(value instanceof HTMLInputElement)) return;

    const hasNoValue = operator.value === "IS NULL" || operator.value === "IS NOT NULL";
    if (hasNoValue) value.value = "";
    value.readOnly = hasNoValue;
    value.placeholder = hasNoValue ? "No value for this operator" : "Value, @InputName, or comma list";
    updateTemplateMirror(value);
  }

  function initializeSqlBuilders(root = document) {
    const builders = [];
    if (root instanceof Element && root.matches("[data-sql-builder]")) {
      builders.push(root);
    }
    root.querySelectorAll?.("[data-sql-builder]").forEach((builder) => builders.push(builder));

    builders.forEach((builder) => {
      const form = builder.closest("[data-app-config-form]");
      if (form) updateSqlModeSections(form);
      builder.querySelectorAll(".sql-filter-row").forEach((row) => {
        if (!row.classList.contains("sql-row-template")) syncSqlFilterRow(row);
      });
    });
  }

  function initializeResourceForms(root = document) {
    const forms = [];
    if (root instanceof Element && root.matches("[data-resource-form]")) {
      forms.push(root);
    }
    root.querySelectorAll?.("[data-resource-form]").forEach((form) => forms.push(form));

    forms.forEach((form) => {
      updateResourceFormSections(form);
      initializeDirtyForm(form);
    });
  }

  function updateDynamicBodySections(form) {
    const checkbox = form.querySelector("[data-dynamic-body-checkbox]");
    const useDynamic =
      checkbox instanceof HTMLInputElement ? checkbox.checked : false;

    form.querySelectorAll("[data-static-body-section]").forEach((section) => {
      section.hidden = useDynamic;
    });
    form.querySelectorAll("[data-dynamic-body-help]").forEach((section) => {
      section.hidden = !useDynamic;
    });
  }

  function inputTypeConfigMeta(inputType) {
    switch (inputType) {
      case "text":
        return {
          visible: true,
          placeholder: "Max length, e.g. 100",
          help: "Optional. Text length defaults to 100 characters.",
        };
      case "radio":
        return {
          visible: true,
          placeholder: "option1, option2",
          help: "Required for radio. Use comma-separated options.",
          defaultValue: "option1, option2",
        };
      case "multi-email":
        return {
          visible: true,
          placeholder: "a@example.com, b@example.com",
          help: "Allowed email choices, comma-separated.",
        };
      case "multi-date":
        return {
          visible: true,
          placeholder: "2026-01-01, 2026-01-02",
          help: "Allowed date choices, comma-separated YYYY-MM-DD values.",
        };
      case "multi-text":
        return {
          visible: true,
          placeholder: "100|option1, option2",
          help: "Use max length, a pipe, then comma-separated text choices.",
        };
      case "multi-integer":
        return {
          visible: true,
          placeholder: "1, 2, 3",
          help: "Allowed integer choices, comma-separated.",
        };
      default:
        return { visible: false, placeholder: "", help: "No type configuration needed." };
    }
  }

  function syncInputRowState(row, options = {}) {
    const typeSelect = row.querySelector("[data-input-type-select]");
    const requiredToggle = row.querySelector("[data-input-required-toggle]");
    const requiredValue = row.querySelector("[data-input-required-value]");
    const requiredLabel = row.querySelector("[data-input-required-label]");
    const defaultShell = row.querySelector("[data-input-default-shell]");
    const defaultInput = row.querySelector('input[name="InputDefaultValue"]');
    const configShell = row.querySelector("[data-input-type-config-shell]");
    const configInput = row.querySelector("[data-input-type-config]");
    const configHelp = row.querySelector("[data-input-type-config-help]");

    if (!(typeSelect instanceof HTMLSelectElement)) return;
    if (!(requiredToggle instanceof HTMLInputElement)) return;
    if (!(requiredValue instanceof HTMLInputElement)) return;

    const isBoolean = typeSelect.value === "boolean";
    if (isBoolean) requiredToggle.checked = true;
    requiredToggle.disabled = isBoolean;

    const isRequired = isBoolean || requiredToggle.checked;
    requiredValue.value = isRequired ? "true" : "false";

    if (requiredLabel) {
      requiredLabel.textContent = isBoolean ? "Required (boolean)" : "Required";
    }

    if (defaultShell instanceof HTMLElement) defaultShell.hidden = isRequired;
    if (isRequired && defaultInput instanceof HTMLInputElement) defaultInput.value = "";

    const configMeta = inputTypeConfigMeta(typeSelect.value);
    if (configShell instanceof HTMLElement) configShell.hidden = !configMeta.visible;
    if (configInput instanceof HTMLInputElement) {
      configInput.placeholder = configMeta.placeholder;
      configInput.setAttribute("aria-label", configMeta.placeholder || "Type configuration");
      if (!configMeta.visible) configInput.value = "";
      if (
        options.typeChanged &&
        configMeta.defaultValue &&
        (!configInput.value || !configInput.value.includes(","))
      ) {
        configInput.value = configMeta.defaultValue;
      }
    }
    if (configHelp) configHelp.textContent = configMeta.help;
  }

  function initializeInputRows(root = document) {
    const rows = [];
    if (root instanceof Element && root.matches(".input-row")) rows.push(root);
    root.querySelectorAll?.(".input-row").forEach((row) => rows.push(row));

    rows.forEach((row) => {
      if (!row.classList.contains("input-row-template")) syncInputRowState(row);
    });
  }

  function shouldTrackControl(control) {
    return (
      control.name &&
      control.name !== "__RequestVerificationToken" &&
      !control.closest(".kv-row-template, .input-row-template, .sql-row-template")
    );
  }

  function dirtySnapshot(form) {
    const controls = Array.from(form.querySelectorAll("input, textarea, select"));
    return JSON.stringify(
      controls.filter(shouldTrackControl).map((control) => {
        if (control instanceof HTMLInputElement) {
          if (control.type === "checkbox" || control.type === "radio") {
            return [control.name, control.type, control.value, control.checked];
          }
          return [control.name, control.type, control.value];
        }
        if (control instanceof HTMLTextAreaElement) {
          return [control.name, "textarea", control.value];
        }
        if (control instanceof HTMLSelectElement) {
          return [control.name, "select", control.value];
        }
        return [control.name, control.value];
      })
    );
  }

  function setDirtyFormState(form, isDirty) {
    const saveButton = form.querySelector("[data-dirty-submit]");
    const resetButton = form.querySelector("[data-dirty-reset]");
    const status = form.querySelector("[data-dirty-status]");

    if (saveButton instanceof HTMLButtonElement) saveButton.disabled = !isDirty;
    if (resetButton instanceof HTMLButtonElement) resetButton.disabled = !isDirty;
    if (status) {
      status.textContent = isDirty ? "Unsaved changes" : "No unsaved changes";
      status.classList.toggle("is-dirty", isDirty);
    }
  }

  function updateDirtyFormState(form) {
    if (!form.matches("[data-track-dirty]")) return;
    const initialSnapshot = form.dataset.initialSnapshot || "";
    setDirtyFormState(form, dirtySnapshot(form) !== initialSnapshot);
  }

  function initializeDirtyForm(form) {
    if (!form.matches("[data-track-dirty]")) return;
    if (!form.dataset.initialHtml) form.dataset.initialHtml = form.innerHTML;
    form.dataset.initialSnapshot = dirtySnapshot(form);
    setDirtyFormState(form, false);
  }

  function resetDirtyForm(form) {
    if (!form.matches("[data-track-dirty]") || !form.dataset.initialHtml) return;
    form.innerHTML = form.dataset.initialHtml;
    updateAppResourceSections(form);
    updateResourceFormSections(form);
    updateSqlModeSections(form);
    updateDynamicBodySections(form);
    initializeInputRows(form);
    initializeSqlBuilders(form);
    initializeTemplateInputs(form);
    initializeDirtyForm(form);
    syncTemplateMirrors(form);
  }

  function initializeAppConfigForms(root = document) {
    const forms = [];
    if (root instanceof Element && root.matches("[data-app-config-form]")) {
      forms.push(root);
    }
    root.querySelectorAll?.("[data-app-config-form]").forEach((form) => forms.push(form));

    forms.forEach((form) => {
      updateAppResourceSections(form);
      updateSqlModeSections(form);
      updateDynamicBodySections(form);
      initializeInputRows(form);
      initializeSqlBuilders(form);
      initializeDirtyForm(form);
    });
  }

  document.addEventListener("input", (event) => {
    const templateInput = closest(event.target, "[data-template-input]");
    if (isTemplateControl(templateInput)) {
      updateTemplateMirror(templateInput);
      maybeOpenTemplatePopover(templateInput);
    }

    const expressionInput = closest(event.target, "[data-expression-editor-input]");
    if (expressionInput) updateExpressionModalValidation();

    const inputTitle = closest(event.target, 'input[name="InputTitle"]');
    if (inputTitle instanceof HTMLInputElement) {
      const form = inputTitle.closest("[data-app-config-form]");
      syncTemplateMirrors(form || document);
      if (activeTemplateInput) maybeOpenTemplatePopover(activeTemplateInput);
    }

    const input = closest(event.target, "[data-confirm-input]");
    if (input) {
      const form = input.closest("[data-typed-confirm-form]");
      if (form) updateTypedConfirmState(form);
    }

    const dirtyForm = closest(event.target, "[data-track-dirty]");
    if (dirtyForm instanceof HTMLFormElement) updateDirtyFormState(dirtyForm);
  });

  document.addEventListener("keydown", (event) => {
    const editableInput = closest(event.target, "[data-editable-name-input]");
    if (editableInput instanceof HTMLInputElement && !editableInput.readOnly) {
      const form = editableInput.closest("[data-editable-name-form]");
      if (form instanceof HTMLFormElement && event.key === "Enter") {
        event.preventDefault();
        submitEditableNameForm(form);
        return;
      }
      if (event.key === "Escape") {
        const initialValue = editableInput.getAttribute("data-initial-value") || editableInput.defaultValue || "";
        editableInput.value = initialValue;
        if (form instanceof HTMLFormElement) {
          clearEditableNameError(form);
          setEditableNameEditing(form, false);
        }
        return;
      }
    }

    const templateInput = closest(event.target, "[data-template-input]");
    if (!isTemplateControl(templateInput)) return;

    if (handleExpressionBraceShortcut(event, templateInput)) return;

    if ((event.metaKey || event.ctrlKey) && event.key === "Enter") {
      const editorInput = closest(event.target, "[data-expression-editor-input]");
      if (editorInput) {
        event.preventDefault();
        saveExpressionModal();
        return;
      }
    }

    const popover = ensureTemplatePopover(templatePopoverOwner(templateInput));
    if (popover.hidden || !activeTemplateTrigger) return;

    if (event.key === "ArrowDown") {
      event.preventDefault();
      activeTemplateSuggestionIndex = Math.min(
        activeTemplateSuggestionIndex + 1,
        Math.max(activeTemplateSuggestions.length - 1, 0)
      );
      renderTemplatePopover(templateInput, activeTemplateTrigger);
      return;
    }

    if (event.key === "ArrowUp") {
      event.preventDefault();
      activeTemplateSuggestionIndex = Math.max(activeTemplateSuggestionIndex - 1, 0);
      renderTemplatePopover(templateInput, activeTemplateTrigger);
      return;
    }

    if (event.key === "Enter" || event.key === "Tab") {
      if (activeTemplateSuggestions.length > 0) {
        event.preventDefault();
        selectTemplateSuggestion(activeTemplateSuggestionIndex);
      }
      return;
    }

    if (event.key === "Escape") {
      event.preventDefault();
      closeTemplatePopover();
    }
  });

  document.addEventListener("focusin", (event) => {
    const templateInput = closest(event.target, "[data-template-input]");
    if (isTemplateControl(templateInput)) updateTemplateMirror(templateInput);
  });

  document.addEventListener("focusout", (event) => {
    const editableInput = closest(event.target, "[data-editable-name-input]");
    if (editableInput instanceof HTMLInputElement && !editableInput.readOnly) {
      const form = editableInput.closest("[data-editable-name-form]");
      if (form instanceof HTMLFormElement) {
        window.setTimeout(() => submitEditableNameForm(form), 0);
      }
    }

    const templateInput = closest(event.target, "[data-template-input]");
    if (!isTemplateControl(templateInput)) return;

    window.setTimeout(() => {
      if (!templatePopover?.contains(document.activeElement)) closeTemplatePopover();
    }, 100);
  });

  document.addEventListener(
    "scroll",
    (event) => {
      if (isTemplateControl(event.target)) updateTemplateMirror(event.target);
      if (activeTemplateInput) positionTemplatePopover(activeTemplateInput);
    },
    true
  );

  document.addEventListener("change", (event) => {
    const permissionCheckbox = closest(event.target, "[data-permission-checkbox]");
    if (permissionCheckbox) {
      const container = permissionCheckbox.closest("[data-permissions-matrix]");
      if (container) updatePermissionsSaveState(container);
      return;
    }

    const addMemberSelect = closest(event.target, "[data-add-member-select]");
    if (addMemberSelect) {
      const form = addMemberSelect.closest("[data-add-member-form]");
      if (form) updateAddMemberSubmitState(form);
      return;
    }

    const autoSubmitControl = closest(event.target, "[data-auto-submit-on-change]");
    if (autoSubmitControl instanceof HTMLSelectElement || autoSubmitControl instanceof HTMLInputElement) {
      requestSubmitOnChange(autoSubmitControl);
    }

    const resourceSelect = closest(event.target, "[data-app-resource-select]");
    if (resourceSelect) {
      const form = resourceSelect.closest("[data-app-config-form]");
      if (form) {
        updateAppResourceSections(form);
        updateDirtyFormState(form);
      }
      return;
    }

    const dynamicBodyCheckbox = closest(event.target, "[data-dynamic-body-checkbox]");
    if (dynamicBodyCheckbox) {
      const form = dynamicBodyCheckbox.closest("[data-app-config-form]");
      if (form) {
        updateDynamicBodySections(form);
        updateDirtyFormState(form);
      }
      return;
    }

    const resourceKindSelect = closest(event.target, "[data-resource-form-kind-select]");
    if (resourceKindSelect) {
      const form = resourceKindSelect.closest("[data-resource-form]");
      if (form) {
        updateResourceFormSections(form);
        updateDirtyFormState(form);
      }
      return;
    }

    const sqlModeControl = closest(event.target, "[data-sql-mode-control]");
    if (sqlModeControl) {
      const form = sqlModeControl.closest("[data-app-config-form]");
      if (form) {
        updateSqlModeSections(form);
        updateDirtyFormState(form);
      }
      return;
    }

    const sqlFilterOperator = closest(event.target, "[data-sql-filter-operator]");
    if (sqlFilterOperator) {
      const row = sqlFilterOperator.closest(".sql-filter-row");
      if (row && !row.classList.contains("sql-row-template")) syncSqlFilterRow(row);
    }

    const inputRowControl = closest(
      event.target,
      "[data-input-type-select], [data-input-required-toggle]"
    );
    if (inputRowControl) {
      const row = inputRowControl.closest(".input-row");
      if (row && !row.classList.contains("input-row-template")) {
        syncInputRowState(row, {
          typeChanged: inputRowControl.matches("[data-input-type-select]"),
        });
      }
    }

    const dirtyForm = closest(event.target, "[data-track-dirty]");
    if (dirtyForm instanceof HTMLFormElement) updateDirtyFormState(dirtyForm);
  });

  document.addEventListener("click", (event) => {
    const target = event.target;
    if (
      !closest(target, ".template-input-shell") &&
      !closest(target, "[data-template-suggestions]")
    ) {
      closeTemplatePopover();
    }

    const expressionSave = closest(target, "[data-expression-modal-save]");
    if (expressionSave) {
      event.preventDefault();
      saveExpressionModal();
      return;
    }

    const expressionCancel = closest(target, "[data-expression-modal-cancel]");
    if (expressionCancel) {
      event.preventDefault();
      closeExpressionModal();
      return;
    }

    const clickedTemplateInput = closest(target, "[data-template-input]");
    if (isTemplateControl(clickedTemplateInput)) {
      window.setTimeout(() => maybeOpenExpressionAtCursor(clickedTemplateInput), 0);
    }

    const editableNameEdit = closest(target, "[data-editable-name-edit]");
    if (editableNameEdit) {
      event.preventDefault();
      const form = editableNameEdit.closest("[data-editable-name-form]");
      if (form instanceof HTMLFormElement) {
        clearEditableNameError(form);
        setEditableNameEditing(form, true, true);
      }
      return;
    }

    const dirtyReset = closest(target, "[data-dirty-reset]");
    if (dirtyReset) {
      event.preventDefault();
      const form = dirtyReset.closest("[data-track-dirty]");
      if (form instanceof HTMLFormElement) resetDirtyForm(form);
      return;
    }

    const addButton = closest(target, "[data-add-kv-row]");
    if (addButton) {
      const container = addButton.closest("[data-kv-rows]");
      const template = container?.querySelector(".kv-row-template");
      if (container && template) {
        const clone = template.cloneNode(true);
        clone.classList.remove("kv-row-template");
        resetFormControls(clone);
        container.insertBefore(clone, addButton);
        initializeTemplateInputs(clone);
        const form = container.closest("[data-track-dirty]");
        if (form instanceof HTMLFormElement) updateDirtyFormState(form);
      }
      return;
    }

    const addInputButton = closest(target, "[data-add-input-row]");
    if (addInputButton) {
      const container = addInputButton.closest("[data-input-rows]");
      const template = container?.querySelector(".input-row-template");
      if (container && template) {
        const clone = template.cloneNode(true);
        clone.classList.remove("input-row-template");
        resetFormControls(clone);
        container.insertBefore(clone, addInputButton);
        initializeTemplateInputs(clone);
        initializeInputRows(clone);
        const form = container.closest("[data-track-dirty]");
        if (form instanceof HTMLFormElement) updateDirtyFormState(form);
      }
      return;
    }

    const addSqlButton = closest(target, "[data-add-sql-row]");
    if (addSqlButton) {
      const container = addSqlButton.closest("[data-sql-rows]");
      const template = container?.querySelector(".sql-row-template");
      if (container && template) {
        const clone = template.cloneNode(true);
        clone.classList.remove("sql-row-template");
        resetFormControls(clone);
        container.insertBefore(clone, addSqlButton);
        initializeTemplateInputs(clone);
        if (clone.matches(".sql-filter-row")) syncSqlFilterRow(clone);
        const form = container.closest("[data-track-dirty]");
        if (form instanceof HTMLFormElement) updateDirtyFormState(form);
      }
      return;
    }

    const removeButton = closest(target, "[data-remove-row]");
    if (removeButton) {
      const row = removeButton.closest(".kv-row, .input-row, .sql-builder-row");
      if (
        row &&
        !row.classList.contains("kv-row-template") &&
        !row.classList.contains("input-row-template") &&
        !row.classList.contains("sql-row-template")
      ) {
        const form = row.closest("[data-track-dirty]");
        const appConfigForm = row.closest("[data-app-config-form]");
        row.remove();
        syncTemplateMirrors(appConfigForm || document);
        if (form instanceof HTMLFormElement) updateDirtyFormState(form);
      }
      return;
    }

    const modalOpen = closest(target, "[data-modal-open]");
    if (modalOpen) {
      event.preventDefault();
      const modalId = modalOpen.getAttribute("data-modal-open");
      const modal = modalId ? document.getElementById(modalId) : null;
      if (modal && typeof modal.showModal === "function") modal.showModal();
      return;
    }

    const modalClose = closest(target, "[data-modal-close]");
    if (modalClose) {
      event.preventDefault();
      const modal = modalClose.closest("dialog");
      if (modal && typeof modal.close === "function") modal.close();
      return;
    }

    const confirmElement = closest(target, "[data-confirm]");
    if (confirmElement) {
      const message = confirmElement.getAttribute("data-confirm") || "Are you sure?";
      if (!window.confirm(message)) event.preventDefault();
    }
  });

  document.addEventListener("cancel", (event) => {
    if (event.target === expressionModal()) {
      event.preventDefault();
      closeExpressionModal();
    }
  });

  initializePermissionsMatrices();
  initializeTypedConfirmForms();
  initializeAddMemberForms();
  initializeEditableNameForms();
  initializeAppConfigForms();
  initializeResourceForms();
  initializeSqlBuilders();
  initializeTemplateInputs();

  const templateInputObserver = new MutationObserver((mutations) => {
    mutations.forEach((mutation) => {
      mutation.addedNodes.forEach((node) => {
        if (node instanceof Element) {
          initializeAddMemberForms(node);
          initializeEditableNameForms(node);
          initializeAppConfigForms(node);
          initializeResourceForms(node);
          initializeSqlBuilders(node);
          initializeTemplateInputs(node);
          initializeInputRows(node);
        }
      });
    });
  });
  templateInputObserver.observe(document.body, { childList: true, subtree: true });
})();
