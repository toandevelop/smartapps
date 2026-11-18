'use client';

import type { FormCondition, FormRule, SysColumn } from '@/lib/types';

const operators: [FormCondition['operator'], string][] = [
  ['EQUALS', 'Bằng'], ['NOT_EQUALS', 'Khác'], ['CONTAINS', 'Chứa'], ['GREATER_THAN', 'Lớn hơn'],
  ['GREATER_OR_EQUAL', 'Lớn hơn hoặc bằng'], ['LESS_THAN', 'Nhỏ hơn'], ['LESS_OR_EQUAL', 'Nhỏ hơn hoặc bằng'],
  ['IS_EMPTY', 'Để trống'], ['NOT_EMPTY', 'Có giá trị'],
];

export default function FormRulesEditor({ columns, rules, onChange }: { columns: SysColumn[]; rules: FormRule[]; onChange: (rules: FormRule[]) => void }) {
  const sources = columns.filter(c => c.showInForm && !c.hiddenForRoles?.length && !c.formula && !['AUTO_NUMBER', 'SUB_TABLE', 'FILE', 'IMAGE'].includes(c.dataType));
  const update = (index: number, change: Partial<FormRule>) => onChange(rules.map((rule, i) => i === index ? { ...rule, ...change } : rule));
  return <section style={{ padding: 14, border: '1px solid var(--border-subtle)', borderRadius: 10, marginBottom: 16 }} aria-label="Quy tắc biểu mẫu có điều kiện">
    <h4 style={{ fontSize: 15, marginBottom: 8 }}>Quy tắc form có điều kiện</h4>
    <p style={{ fontSize: 12, color: 'var(--text-secondary)', marginBottom: 12 }}>Mỗi trường có tối đa một quy tắc cho từng thuộc tính. “Mặc định” dùng cấu hình cột. Quy tắc không nới quyền ẩn/chỉ đọc theo vai trò; điều kiện dùng giá trị nhập, không dùng cột tính hoặc cột bị ẩn theo vai trò.</p>
    <p style={{ fontSize: 12, color: 'var(--text-secondary)', marginBottom: 12 }}>Trường chuyển sang ẩn/chỉ đọc giữ giá trị đã lưu hoặc mặc định. Trường ẩn không bắt buộc nhập; trường vừa bắt buộc vừa chỉ đọc cần có giá trị hợp lệ.</p>
    {!columns.length && <p>Tạo bảng và các cột trước, sau đó mở sửa bảng để cấu hình quy tắc.</p>}
    {rules.map((rule, index) => <fieldset key={rule.id} style={{ padding: 12, marginBottom: 14, border: '1px solid var(--border-subtle)', borderRadius: 8 }}>
      <legend>Quy tắc {index + 1}</legend>
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(160px,1fr))', gap: 10 }}>
        <label className="form-label">Trường đích<select className="form-input" value={rule.targetField} onChange={e => update(index, { targetField: e.target.value })}>
          <option value="">Chọn trường</option>{columns.map(c => <option key={c.columnCode} value={c.columnCode}>{c.columnName}</option>)}
        </select></label>
        <label className="form-label">Thuộc tính<select className="form-input" value={rule.property} onChange={e => update(index, { property: e.target.value as FormRule['property'] })}>
          <option value="visible">Hiển thị</option><option value="required">Bắt buộc nhập</option><option value="readOnly">Chỉ đọc</option>
        </select></label>
        <label className="form-label">Khi thỏa điều kiện<select className="form-input" value={String(rule.value)} onChange={e => update(index, { value: e.target.value === 'true' })}>
          <option value="true">Có</option><option value="false">Không</option>
        </select></label>
        <label className="form-label">Nếu không thỏa<select className="form-input" value={rule.elseValue == null ? 'default' : String(rule.elseValue)} onChange={e => update(index, { elseValue: e.target.value === 'default' ? null : e.target.value === 'true' })}>
          <option value="default">Mặc định của cột</option><option value="true">Có</option><option value="false">Không</option>
        </select></label>
      </div>
      <label className="form-label" style={{ marginTop: 12 }}>Kết hợp điều kiện<select className="form-input" value={rule.match} onChange={e => update(index, { match: e.target.value as 'ALL' | 'ANY' })}>
        <option value="ALL">Thỏa tất cả (AND)</option><option value="ANY">Thỏa một điều kiện (OR)</option>
      </select></label>
      {rule.conditions.map((condition, ci) => {
        const source = sources.find(c => c.columnCode === condition.field);
        const type = source?.dataType;
        const set = (change: Partial<FormCondition>) => update(index, { conditions: rule.conditions.map((c, i) => i === ci ? { ...c, ...change } : c) });
        const allowed = operators.filter(([op]) => ['GREATER_THAN', 'GREATER_OR_EQUAL', 'LESS_THAN', 'LESS_OR_EQUAL'].includes(op)
          ? ['NUMBER', 'CURRENCY', 'DATE'].includes(type || '') : op === 'CONTAINS' ? !['NUMBER', 'CURRENCY', 'BOOLEAN', 'DATE'].includes(type || '') : true);
        return <div key={ci} style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'end', gap: 8, margin: '10px 0' }}>
          <label className="form-label" style={{ flex: '1 1 160px' }}>Trường điều kiện {ci + 1}<select className="form-input" value={condition.field} onChange={e => set({ field: e.target.value, operator: 'EQUALS', value: '' })}>
            <option value="">Chọn trường</option>{sources.filter(c => c.columnCode !== rule.targetField).map(c => <option key={c.columnCode} value={c.columnCode}>{c.columnName}</option>)}
          </select></label>
          <label className="form-label" style={{ flex: '1 1 150px' }}>So sánh<select className="form-input" value={condition.operator} onChange={e => set({ operator: e.target.value as FormCondition['operator'] })}>
            {allowed.map(([op, label]) => <option key={op} value={op}>{label}</option>)}
          </select></label>
          {!['IS_EMPTY', 'NOT_EMPTY'].includes(condition.operator) && <label className="form-label" style={{ flex: '1 1 160px' }}>Giá trị {type === 'SELECT' || type === 'LOOKUP' ? '(mã)' : ''}
            {type === 'BOOLEAN' ? <select className="form-input" value={condition.value || ''} onChange={e => set({ value: e.target.value })}>
              <option value="">Chọn giá trị</option><option value="true">Có / true</option><option value="false">Không / false</option>
            </select> : <input className="form-input" type={type === 'DATE' ? 'date' : ['NUMBER', 'CURRENCY'].includes(type || '') ? 'number' : 'text'} step="any" maxLength={500} value={condition.value || ''} onChange={e => set({ value: e.target.value })} />}
          </label>}
          <button type="button" className="btn btn-outline btn-sm" aria-label={`Xóa điều kiện ${ci + 1} của quy tắc ${index + 1}`} disabled={rule.conditions.length <= 1} onClick={() => update(index, { conditions: rule.conditions.filter((_, i) => i !== ci) })}>Xóa điều kiện</button>
        </div>;
      })}
      <div style={{ display: 'flex', gap: 8, marginTop: 12 }}>
        <button type="button" className="btn btn-outline btn-sm" disabled={rule.conditions.length >= 10} onClick={() => update(index, { conditions: [...rule.conditions, { field: '', operator: 'EQUALS', value: '' }] })}>Thêm điều kiện</button>
        <button type="button" className="btn btn-outline btn-sm" onClick={() => onChange(rules.filter((_, i) => i !== index))}>Xóa quy tắc</button>
      </div>
    </fieldset>)}
    <button type="button" className="btn btn-outline btn-sm" disabled={columns.length < 2 || !sources.length || rules.length >= 50} onClick={() => onChange([...rules, {
      id: typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function' ? crypto.randomUUID() : `rule_${Date.now()}_${Math.random().toString(36).slice(2)}`, targetField: columns.find(c => c.columnCode !== sources[0].columnCode)?.columnCode || '',
      property: 'visible', value: true, elseValue: false, match: 'ALL', conditions: [{ field: sources[0].columnCode, operator: 'EQUALS', value: '' }],
    }])}>Thêm quy tắc form</button>
  </section>;
}
