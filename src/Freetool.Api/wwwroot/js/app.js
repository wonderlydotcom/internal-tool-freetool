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

        const requiredSelect = row.querySelector('select[name="InputRequired"]');
        const required =
          requiredSelect instanceof HTMLSelectElement &&
          requiredSelect.value === "true";

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
        html += `<span class="template-token template-token-expression">${escapeHtml(token)}</span>`;
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
    if (control.closest(".kv-row-template, .input-row-template")) return;

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

  function ensureTemplatePopover() {
    if (templatePopover) return templatePopover;

    templatePopover = document.createElement("div");
    templatePopover.className = "template-suggestions";
    templatePopover.setAttribute("data-template-suggestions", "true");
    templatePopover.hidden = true;
    document.body.appendChild(templatePopover);
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
    const popover = ensureTemplatePopover();
    const rect = control.getBoundingClientRect();
    popover.style.left = `${rect.left}px`;
    popover.style.top = `${rect.bottom + 6}px`;
    popover.style.minWidth = `${Math.min(Math.max(rect.width, 260), 420)}px`;
  }

  function renderTemplatePopover(control, trigger) {
    const popover = ensureTemplatePopover();
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

  function updateAppResourceSections(form) {
    const select = form.querySelector("[data-app-resource-select]");
    if (!(select instanceof HTMLSelectElement)) return;

    const selectedOption = select.options[select.selectedIndex];
    const resourceKind =
      selectedOption?.getAttribute("data-resource-kind") || "http";

    form.querySelectorAll("[data-resource-kind-section]").forEach((section) => {
      const sectionKind = section.getAttribute("data-resource-kind-section");
      section.hidden = sectionKind !== resourceKind;
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

  function initializeAppConfigForms() {
    document.querySelectorAll("[data-app-config-form]").forEach((form) => {
      updateAppResourceSections(form);
      updateDynamicBodySections(form);
    });
  }

  document.addEventListener("input", (event) => {
    const templateInput = closest(event.target, "[data-template-input]");
    if (isTemplateControl(templateInput)) {
      updateTemplateMirror(templateInput);
      maybeOpenTemplatePopover(templateInput);
    }

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
  });

  document.addEventListener("keydown", (event) => {
    const templateInput = closest(event.target, "[data-template-input]");
    if (!isTemplateControl(templateInput)) return;

    const popover = ensureTemplatePopover();
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

    const resourceSelect = closest(event.target, "[data-app-resource-select]");
    if (resourceSelect) {
      const form = resourceSelect.closest("[data-app-config-form]");
      if (form) updateAppResourceSections(form);
      return;
    }

    const dynamicBodyCheckbox = closest(event.target, "[data-dynamic-body-checkbox]");
    if (dynamicBodyCheckbox) {
      const form = dynamicBodyCheckbox.closest("[data-app-config-form]");
      if (form) updateDynamicBodySections(form);
    }
  });

  document.addEventListener("click", (event) => {
    const target = event.target;
    if (
      !closest(target, ".template-input-shell") &&
      !closest(target, "[data-template-suggestions]")
    ) {
      closeTemplatePopover();
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
      }
      return;
    }

    const removeButton = closest(target, "[data-remove-row]");
    if (removeButton) {
      const row = removeButton.closest(".kv-row, .input-row");
      if (
        row &&
        !row.classList.contains("kv-row-template") &&
        !row.classList.contains("input-row-template")
      ) {
        const form = row.closest("[data-app-config-form]");
        row.remove();
        syncTemplateMirrors(form || document);
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

  initializePermissionsMatrices();
  initializeTypedConfirmForms();
  initializeAppConfigForms();
  initializeTemplateInputs();

  const templateInputObserver = new MutationObserver((mutations) => {
    mutations.forEach((mutation) => {
      mutation.addedNodes.forEach((node) => {
        if (node instanceof Element) initializeTemplateInputs(node);
      });
    });
  });
  templateInputObserver.observe(document.body, { childList: true, subtree: true });
})();
