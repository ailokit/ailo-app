-- Translate application-owned built-in skill text to the repository language.
UPDATE Skills
SET Name = CASE Id
        WHEN 'builtin-chat' THEN 'General chat'
        WHEN 'builtin-translate' THEN 'Translation'
        WHEN 'builtin-summarize' THEN 'Summarization'
        WHEN 'builtin-code-explain' THEN 'Code explanation'
        WHEN 'builtin-polish' THEN 'Writing polish'
        ELSE Name
    END,
    Description = CASE Id
        WHEN 'builtin-chat' THEN 'A general-purpose AI assistant'
        WHEN 'builtin-translate' THEN 'Natural and faithful translation'
        WHEN 'builtin-summarize' THEN 'Concise and accurate summaries'
        WHEN 'builtin-code-explain' THEN 'Explain code logic and design'
        WHEN 'builtin-polish' THEN 'Improve wording and expression'
        ELSE Description
    END,
    SystemPrompt = CASE Id
        WHEN 'builtin-chat' THEN 'You are Ailo, a helpful AI assistant.'
        WHEN 'builtin-translate' THEN 'You are a professional translation assistant. Preserve the original meaning and produce natural, fluent translations.'
        WHEN 'builtin-summarize' THEN 'Summarize the input concisely and accurately.'
        WHEN 'builtin-code-explain' THEN 'Explain the code logic and key design decisions in clear, accessible language.'
        WHEN 'builtin-polish' THEN 'Improve the wording and correct grammar while preserving the original meaning.'
        ELSE SystemPrompt
    END
WHERE IsBuiltIn = 1
  AND Id IN ('builtin-chat', 'builtin-translate', 'builtin-summarize', 'builtin-code-explain', 'builtin-polish');
