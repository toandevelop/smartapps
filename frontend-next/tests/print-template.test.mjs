import test from 'node:test';
import assert from 'node:assert/strict';
import {
  getPagePrintCss,
  numberToVietnameseWords,
  renderPrintTemplateHtml,
  sanitizePrintHtml,
} from '../src/lib/print-template.ts';
import {
  compilePrintBuilderToHtml,
  createDefaultBuilderSchema,
  parseBuilderSchemaFromHtml,
} from '../src/lib/print-builder.ts';

test('numberToVietnameseWords formats Vietnamese currency amounts accurately', () => {
  assert.equal(numberToVietnameseWords(0), 'Không đồng');
  assert.equal(numberToVietnameseWords(1250000), 'Một triệu hai trăm năm mươi nghìn đồng chẵn');
  assert.equal(numberToVietnameseWords(105000), 'Một trăm lẻ năm nghìn đồng chẵn');
});

test('renderPrintTemplateHtml renders sub-table loops, pipes, and escapes HTML XSS', () => {
  const tpl = `<h1>{{record_key}}</h1><p>{{khach_hang}}</p><ul>{{#each chi_tiet}}<li>{{@index1}}. {{ten}} - {{tien | currency}}</li>{{/each}}</ul><div>{{tong | vnd_words}}</div>`;
  const html = renderPrintTemplateHtml(tpl, {
    appName: 'Sales',
    tableName: 'Orders',
    recordKey: 'DH-01',
    data: {
      khach_hang: '<script>alert(1)</script>',
      tong: 250000,
      chi_tiet: [
        { ten: 'Bàn phím', tien: 150000 },
        { ten: 'Chuột', tien: 100000 },
      ],
    },
  });

  assert.ok(html.includes('<h1>DH-01</h1>'));
  assert.ok(html.includes('&lt;script&gt;alert(1)&lt;/script&gt;'));
  assert.ok(!html.includes('<script>'));
  assert.ok(html.includes('1. Bàn phím'));
  assert.ok(html.includes('2. Chuột'));
  assert.ok(html.includes('Hai trăm năm mươi nghìn đồng chẵn'));
});

test('renderPrintTemplateHtml supports #if/#else, #sum, barcode, image, boolean pipes', () => {
  const tpl = `
    {{#if ghi_chu}}<p>Note: {{ghi_chu}}</p>{{else}}<p>No note</p>{{/if}}
    <div>Sum: {{#sum chi_tiet.tien | currency}}</div>
    <div>{{barcode:record_key}}</div>
    <div>{{anh | image}}</div>
    <div>{{da_thanh_toan | boolean}}</div>
  `;
  const html = renderPrintTemplateHtml(tpl, {
    appName: 'Sales',
    tableName: 'Orders',
    recordKey: 'DH-99',
    data: {
      ghi_chu: '',
      da_thanh_toan: true,
      anh: 'https://example.com/logo.png',
      chi_tiet: [{ tien: 100000 }, { tien: 200000 }],
    },
  });

  assert.ok(html.includes('<p>No note</p>'));
  assert.ok(!html.includes('Note:'));
  assert.ok(html.includes('300.000 đ'));
  assert.ok(html.includes('<svg viewBox='));
  assert.ok(html.includes('<img src="https://example.com/logo.png"'));
  assert.ok(html.includes('Có'));
});

test('sanitizePrintHtml strips dangerous tags and event handlers', () => {
  const dirty = `<div onclick="alert(1)"><script>evil()</script><iframe src="x"></iframe><a href="javascript:alert(2)">link</a></div>`;
  const clean = sanitizePrintHtml(dirty);
  assert.ok(!clean.includes('<script'));
  assert.ok(!clean.includes('<iframe'));
  assert.ok(!clean.includes('onclick'));
  assert.ok(!clean.includes('javascript:'));
});

test('getPagePrintCss generates @page size and orientation rules', () => {
  assert.ok(getPagePrintCss('A4', 'landscape').includes('size: A4 landscape'));
  assert.ok(getPagePrintCss('K80', 'portrait').includes('size: 80mm auto'));
  assert.ok(getPagePrintCss('LABEL_50X30', 'landscape').includes('size: 50mm 30mm'));
});

test('print-builder compiles schema to HTML and round-trips via parseBuilderSchemaFromHtml', () => {
  const schema = createDefaultBuilderSchema('Phiếu Nhập Kho', [
    { columnCode: 'nha_cung_cap', columnName: 'Nhà cung cấp', dataType: 'STRING' },
    { columnCode: 'tong_tien', columnName: 'Tổng tiền', dataType: 'CURRENCY' },
    { columnCode: 'chi_tiet', columnName: 'Chi tiết hàng', dataType: 'SUB_TABLE' },
  ]);
  const compiled = compilePrintBuilderToHtml(schema);
  const parsed = parseBuilderSchemaFromHtml(compiled);

  assert.ok(parsed !== null);
  assert.equal(parsed?.version, 1);
  assert.equal(parsed?.blocks.length, schema.blocks.length);

  const rendered = renderPrintTemplateHtml(compiled, {
    appName: 'Kho',
    appDepartment: 'Vận hành',
    tableName: 'Phiếu Nhập Kho',
    recordKey: 'PNK-001',
    data: {
      nha_cung_cap: 'Công ty ABC',
      tong_tien: 500000,
      chi_tiet: [{ ten_hang: 'Thép hộp', so_luong: 10, thanh_tien: 500000 }],
    },
  });

  assert.ok(!rendered.includes('SMARTAPPS_BUILDER_V1'));
  assert.ok(rendered.includes('Công ty ABC'));
  assert.ok(rendered.includes('Thép hộp'));
  assert.ok(rendered.includes('Năm trăm nghìn đồng chẵn'));
});
