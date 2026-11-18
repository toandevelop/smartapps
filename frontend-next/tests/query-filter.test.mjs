import assert from 'node:assert/strict';
import test from 'node:test';
import {
  buildEffectiveFiltersPayload,
  describeCondition,
  getOperatorsForColumn,
  isStructuredFilter,
  normalizeSavedViewFilters,
} from '../src/lib/query-filter.ts';

test('normalizeSavedViewFilters handles both legacy flat dicts and structured AND/OR filters', () => {
  const legacy = normalizeSavedViewFilters({ department: ' IT ', emptyField: '   ' });
  assert.deepEqual(legacy.flatFilters, { department: 'IT' });
  assert.equal(legacy.structured.conditions.length, 0);

  const structured = normalizeSavedViewFilters({
    logic: 'OR',
    conditions: [
      { field: 'amount', op: 'gte', value: '5000000' },
      { field: 'approvalStatus', op: 'contains', value: 'PENDING' },
      { field: 'note', op: 'contains', value: '   ' },
    ],
  });
  assert.deepEqual(structured.flatFilters, {});
  assert.equal(structured.structured.logic, 'OR');
  assert.deepEqual(structured.structured.conditions, [
    { field: 'amount', op: 'gte', value: '5000000' },
    { field: 'approvalStatus', op: 'eq', value: 'PENDING' },
  ]);
});

test('buildEffectiveFiltersPayload prunes incomplete conditions and handles AND merging cleanly', () => {
  assert.equal(buildEffectiveFiltersPayload({}, { logic: 'AND', conditions: [] }), null);

  assert.deepEqual(
    buildEffectiveFiltersPayload({ city: 'HCM' }, { logic: 'AND', conditions: [] }),
    { city: 'HCM' },
  );

  const mergedAnd = buildEffectiveFiltersPayload(
    { city: 'HCM', approvalStatus: 'APPROVED' },
    {
      logic: 'AND',
      conditions: [
        { field: 'amount', op: 'between', value: '100', value2: '500' },
        { field: 'badBetween', op: 'between', value: '100', value2: '' },
        { field: 'email', op: 'is_empty' },
      ],
    },
  );
  assert.equal(isStructuredFilter(mergedAnd), true);
  assert.deepEqual(mergedAnd, {
    logic: 'AND',
    conditions: [
      { field: 'amount', op: 'between', value: '100', value2: '500' },
      { field: 'email', op: 'is_empty' },
      { field: 'city', op: 'contains', value: 'HCM' },
      { field: 'approvalStatus', op: 'eq', value: 'APPROVED' },
    ],
  });

  // Do not append flat AND filters inside an OR group (would alter boolean precedence).
  const pureOr = buildEffectiveFiltersPayload(
    { city: 'HCM' },
    {
      logic: 'OR',
      conditions: [
        { field: 'status', op: 'eq', value: 'URGENT' },
        { field: 'priority', op: 'eq', value: 'HIGH' },
      ],
    },
  );
  assert.deepEqual(pureOr, {
    logic: 'OR',
    conditions: [
      { field: 'status', op: 'eq', value: 'URGENT' },
      { field: 'priority', op: 'eq', value: 'HIGH' },
    ],
  });
});

test('getOperatorsForColumn and describeCondition reflect field capabilities', () => {
  assert.deepEqual(
    getOperatorsForColumn('approvalStatus').map((o) => o.value),
    ['eq'],
  );
  assert.ok(getOperatorsForColumn('salary', 'CURRENCY').some((o) => o.value === 'between'));
  assert.ok(getOperatorsForColumn('joined_at', 'DATE').some((o) => o.value === 'date_preset'));

  const cols = [
    { id: '1', tableId: 't1', columnCode: 'due_date', columnName: 'Hạn xử lý', excelHeader: 'Hạn', dataType: 'DATE', isRequired: false, isIdentityKey: false, showInTable: true, showInForm: true, width: 150, orderIndex: 1 },
  ];
  assert.equal(
    describeCondition({ field: 'due_date', op: 'date_preset', value: 'THIS_WEEK' }, cols),
    'Hạn xử lý: Tuần này',
  );
  assert.equal(
    describeCondition({ field: 'due_date', op: 'not_empty' }, cols),
    'Hạn xử lý có dữ liệu',
  );
});
