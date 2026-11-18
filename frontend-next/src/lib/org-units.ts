import type { SysOrgUnit } from './types.ts';

export interface OrgTreeNode {
  unit: SysOrgUnit;
  depth: number;
}

/**
 * Sorts organizational units into a depth-first hierarchy ordered by orderIndex then name.
 * Guards against orphan parentIds and parent-child cycles.
 */
export function flattenOrgUnits(units: SysOrgUnit[]): OrgTreeNode[] {
  const byParent = new Map<string, SysOrgUnit[]>();
  const validIds = new Set(units.map((u) => u.id));
  for (const u of units) {
    const pid = u.parentId && validIds.has(u.parentId) ? u.parentId : '__ROOT__';
    const arr = byParent.get(pid) || [];
    arr.push(u);
    byParent.set(pid, arr);
  }
  const rows: OrgTreeNode[] = [];
  const seen = new Set<string>();
  const visit = (pid: string, depth: number) => {
    const children = (byParent.get(pid) || [])
      .slice()
      .sort((a, b) => (a.orderIndex ?? 0) - (b.orderIndex ?? 0) || a.name.localeCompare(b.name, 'vi'));
    for (const c of children) {
      if (seen.has(c.id)) continue;
      seen.add(c.id);
      rows.push({ unit: c, depth });
      visit(c.id, depth + 1);
    }
  };
  visit('__ROOT__', 0);
  // Append any disconnected cyclic components as root fallback so nothing disappears
  for (const u of units) {
    if (!seen.has(u.id)) {
      seen.add(u.id);
      rows.push({ unit: u, depth: 0 });
      visit(u.id, 1);
    }
  }
  return rows;
}
