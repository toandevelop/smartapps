import test from 'node:test';
import assert from 'node:assert/strict';
import {
  numberToVietnameseWords,
  renderPrintTemplateHtml,
} from '../src/lib/print-template.ts';

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
