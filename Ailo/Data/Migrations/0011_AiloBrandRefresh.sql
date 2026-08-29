-- Refresh application-owned values that are stored inside the user database.
UPDATE Skills
SET SystemPrompt = replace(SystemPrompt, 'Chater', 'Ailo')
WHERE IsBuiltIn = 1 AND instr(SystemPrompt, 'Chater') > 0;

UPDATE Messages
SET Content = replace(
    replace(Content, '<!-- chater-tool -->', '<!-- ailo-tool -->'),
    '<!-- /chater-tool -->',
    '<!-- /ailo-tool -->')
WHERE instr(Content, '<!-- chater-tool -->') > 0
   OR instr(Content, '<!-- /chater-tool -->') > 0;
