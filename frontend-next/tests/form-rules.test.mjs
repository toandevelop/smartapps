import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import ts from 'typescript';

const source = await readFile(new URL('../src/lib/form-rules.ts', import.meta.url), 'utf8');
const compiled = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 } }).outputText;
const { matchesFormCondition, evaluateFormRules, prepareFormSubmission, requiredFormErrors, restoreProtectedFormValues, formDefaultValues } = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString('base64')}`);
const defaultCases = JSON.parse(await readFile(new URL('../../backend-dotnet.Tests/Fixtures/form-default-cases.json', import.meta.url), 'utf8'));
for (const item of defaultCases) test(`shared default: ${item.name}`, () => {
  const defaults = formDefaultValues([{ columnCode: 'source', dataType: item.type, defaultValue: item.raw }]);
  assert.deepEqual(defaults, item.omitted ? {} : { source: item.expected });
});
const cases = JSON.parse(await readFile(new URL('../../backend-dotnet.Tests/Fixtures/form-rule-cases.json', import.meta.url), 'utf8'));
for (const item of cases) test(`shared condition: ${item.name}`, () => {
  assert.equal(matchesFormCondition({ field: 'source', operator: item.operator, value: item.expectedValue }, item.type, item.actual), item.expected);
});
const columns = [
  { columnCode: 'kind', columnName: 'Kind', dataType: 'STRING', showInForm: true, isRequired: false },
  { columnCode: 'amount', columnName: 'Amount', dataType: 'NUMBER', showInForm: true, isRequired: false },
  { columnCode: 'locked', columnName: 'Locked', dataType: 'BOOLEAN', showInForm: true, isRequired: false },
  { columnCode: 'note', columnName: 'Note', dataType: 'STRING', showInForm: true, isRequired: false },
];
const rule = (property, field, operator, value) => ({ id: property, targetField: 'note', property, value: true, elseValue: false, match: 'ALL', conditions: [{ field, operator, value }] });
test('conditions update required/visibility immediately and reset when they stop matching', () => {
  const rules = [rule('visible', 'kind', 'EQUALS', 'BUSINESS'), rule('required', 'amount', 'GREATER_THAN', '100')];
  assert.deepEqual(evaluateFormRules(columns, rules, { kind: 'BUSINESS', amount: 200 }).note, { visible: true, required: true, readOnly: false });
  assert.equal(requiredFormErrors(columns, rules, { kind: 'BUSINESS', amount: 200 }).length, 1);
  assert.equal(requiredFormErrors(columns, rules, { kind: 'PERSONAL', amount: 200 }).length, 0);
  assert.equal(evaluateFormRules(columns, rules, { kind: 'PERSONAL', amount: 1 }).note.required, false);
});
test('ANY conditions and null else restore the original column baseline', () => {
  const required = { ...rule('required', 'amount', 'GREATER_THAN', '100'), match: 'ANY', elseValue: null,
    conditions: [{ field: 'amount', operator: 'GREATER_THAN', value: '100' }, { field: 'kind', operator: 'EQUALS', value: 'BUSINESS' }] };
  assert.equal(evaluateFormRules(columns, [required], { amount: 1, kind: 'BUSINESS' }).note.required, true);
  assert.equal(evaluateFormRules(columns, [required], { amount: 1, kind: 'PERSONAL' }).note.required, false);
});
test('submission omits hidden/readonly edits without mutating the draft or losing unruled data', () => {
  const rules = [rule('visible', 'kind', 'EQUALS', 'BUSINESS'), rule('readOnly', 'locked', 'EQUALS', 'true')];
  const draft = { kind: 'PERSONAL', locked: false, note: 'unsaved edit' };
  assert.equal(prepareFormSubmission(columns, rules, draft, { locked: true, note: 'stored' }).note, undefined);
  assert.equal(draft.note, 'unsaved edit');
  assert.equal(prepareFormSubmission(columns, [], draft).note, 'unsaved edit');
});

test('restoring a hidden field also reevaluates dependent rules against the stored value', () => {
  const chainColumns = [...columns, { columnCode: 'details', columnName: 'Details', dataType: 'STRING', showInForm: true, isRequired: false }];
  const rules = [
    { ...rule('visible', 'kind', 'EQUALS', 'BUSINESS'), targetField: 'details' },
    rule('required', 'details', 'EQUALS', 'STORED'),
  ];
  const original = { kind: 'BUSINESS', details: 'STORED', note: '' };
  const draft = { kind: 'PERSONAL', details: 'UNSAVED', note: '' };
  const restored = restoreProtectedFormValues(chainColumns, rules, draft, original);
  assert.equal(restored.details, 'STORED');
  assert.equal(draft.details, 'UNSAVED');
  assert.equal(requiredFormErrors(chainColumns, rules, draft, original).length, 1);
  assert.equal(prepareFormSubmission(chainColumns, rules, draft, original).details, undefined);
  assert.equal(restoreProtectedFormValues(chainColumns, rules, restored, original), restored);
});

test('new personal forms preserve server-bound defaults while omitting hidden fields from user input', () => {
  const rules = [rule('visible', 'kind', 'EQUALS', 'BUSINESS')];
  const draft = { kind: 'PERSONAL', note: 'SPOOF' };
  const restored = restoreProtectedFormValues(columns, rules, draft, null, { note: 'EMP-RULE' });
  assert.equal(restored.note, 'EMP-RULE');
  assert.equal(prepareFormSubmission(columns, rules, draft, null, { note: 'EMP-RULE' }).note, undefined);
});

test('client required checks leave formula and auto-number values to the backend', () => {
  const fields = [...columns,
    { columnCode: 'auto', columnName: 'Auto', dataType: 'AUTO_NUMBER', showInForm: true, isRequired: true, defaultValue: 'REC-{YYYY}-' },
    { columnCode: 'computed', columnName: 'Computed', dataType: 'NUMBER', showInForm: true, isRequired: true, formula: 'amount * 2' },
  ];
  assert.deepEqual(requiredFormErrors(fields, [], { amount: 10 }), []);
});

test('normalized boolean and text defaults protect readonly fields in a new form', () => {
  const fields = columns.map(c => c.columnCode === 'locked' ? { ...c, defaultValue: '1' }
    : c.columnCode === 'note' ? { ...c, defaultValue: ' SERVER ' } : c);
  const rules = [rule('readOnly', 'locked', 'EQUALS', 'true')];
  const defaults = formDefaultValues(fields);
  assert.equal(evaluateFormRules(fields, rules, defaults).note.readOnly, true);
  const restored = restoreProtectedFormValues(fields, rules, { ...defaults, note: 'UNSAVED' });
  assert.equal(restored.note, 'SERVER');
  assert.equal(prepareFormSubmission(fields, rules, restored).note, undefined);
});
