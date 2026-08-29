UPDATE Messages
SET Content = '````thinking' || char(10) || Reasoning || char(10) || '````' || char(10) || Content,
    Reasoning = ''
WHERE length(trim(Reasoning)) > 0
  AND instr(Content, '````thinking') = 0;
