import assert from 'node:assert/strict';
import test from 'node:test';
import { flattenOrgUnits } from '../src/lib/org-units.ts';

test('flattenOrgUnits orders roots and nested children by depth and orderIndex', () => {
  const units = [
    { id: 'c2', code: 'SALE_NORTH', name: 'Miền Bắc', parentId: 'root', orderIndex: 2, isActive: true },
    { id: 'root', code: 'KD', name: 'Khối Kinh doanh', parentId: null, orderIndex: 1, isActive: true },
    { id: 'c1', code: 'SALE_SOUTH', name: 'Miền Nam', parentId: 'root', orderIndex: 1, isActive: true },
    { id: 'leaf', code: 'TEAM_HN', name: 'Nhóm Hà Nội', parentId: 'c2', orderIndex: 1, isActive: true },
  ];

  const flat = flattenOrgUnits(units);
  assert.deepEqual(
    flat.map((n) => [n.unit.id, n.depth]),
    [
      ['root', 0],
      ['c1', 1],
      ['c2', 1],
      ['leaf', 2],
    ],
  );
});

test('flattenOrgUnits handles orphan parentIds and cycles without infinite recursion', () => {
  const units = [
    { id: 'a', code: 'A', name: 'Unit A', parentId: 'b', orderIndex: 1, isActive: true },
    { id: 'b', code: 'B', name: 'Unit B', parentId: 'a', orderIndex: 2, isActive: true },
    { id: 'orphan', code: 'O', name: 'Orphan', parentId: 'missing_parent', orderIndex: 1, isActive: true },
  ];

  const flat = flattenOrgUnits(units);
  assert.equal(flat.length, 3);
  assert.ok(flat.some((n) => n.unit.id === 'orphan' && n.depth === 0));
});
