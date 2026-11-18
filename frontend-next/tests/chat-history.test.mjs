import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import ts from 'typescript';

// Exercise the actual TypeScript helper without adding a separate test runtime.
const source = await readFile(new URL('../src/lib/chat-history.ts', import.meta.url), 'utf8');
const compiled = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 } }).outputText;
const { mergeChatHistory } = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString('base64')}`);
const message = (id, createdAt, content = id) => ({ id, createdAt, content });

test('prepending a page deduplicates overlap and retains the newest data for incoming IDs', () => {
  const result = mergeChatHistory([
    message('b', '2026-11-17T01:00:01Z'), message('c', '2026-11-17T01:00:02Z'),
  ], [message('a', '2026-11-17T01:00:00Z'), message('b', '2026-11-17T01:00:01Z', 'updated')]);
  assert.deepEqual(result.map(m => m.id), ['a', 'b', 'c']);
  assert.equal(result[1].content, 'updated');
});

test('latest refresh preserves loaded history, updates messages, and removes deletions in the refreshed window', () => {
  const previous = ['a', 'b', 'c', 'd'].map((id, i) => message(id, `2026-11-17T01:00:0${i}Z`));
  const result = mergeChatHistory(previous, [
    message('c', '2026-11-17T01:00:02Z', 'reaction changed'), message('e', '2026-11-17T01:00:04Z'),
  ], true);
  assert.deepEqual(result.map(m => m.id), ['a', 'b', 'c', 'e']);
  assert.equal(result[2].content, 'reaction changed');
});

test('sorting preserves sub-millisecond timestamp precision and uses ID for ties', () => {
  const result = mergeChatHistory([], [
    message('b', '2026-11-17T01:00:00.123457Z'),
    message('z', '2026-11-17T01:00:00.123456Z'),
    message('a', '2026-11-17T01:00:00.123456Z'),
    message('whole', '2026-11-17T01:00:00Z'),
  ]);
  assert.deepEqual(result.map(m => m.id), ['whole', 'a', 'z', 'b']);
});

test('an empty latest window clears deleted history; an empty older page preserves it', () => {
  const previous = [message('a', '2026-11-17T01:00:00Z')];
  assert.deepEqual(mergeChatHistory(previous, [], true), []);
  assert.deepEqual(mergeChatHistory(previous, []), previous);
});
