'use client';

import React, { useId, useState } from 'react';
import AutoAwesomeRoundedIcon from '@mui/icons-material/AutoAwesomeRounded';
import UploadFileRoundedIcon from '@mui/icons-material/UploadFileRounded';

interface AiOcrResponse {
  fields: Record<string, unknown>;
  subTables?: Record<string, Array<Record<string, unknown>>>;
  confidence?: number;
  warnings?: string[];
  engine?: string;
}

export default function AiOcrButton({
  tableId,
  onExtracted,
}: {
  tableId: string;
  onExtracted: (result: AiOcrResponse) => void;
}) {
  const inputId = useId();
  const [openPanel, setOpenPanel] = useState(false);
  const [rawText, setRawText] = useState('');
  const [fileName, setFileName] = useState('');
  const [imageBase64, setImageBase64] = useState('');
  const [mimeType, setMimeType] = useState('');
  const [busy, setBusy] = useState(false);
  const [statusMsg, setStatusMsg] = useState('');

  const handleFilePick = async (file?: File) => {
    if (!file) return;
    if (file.size > 6 * 1024 * 1024) {
      setStatusMsg('Tệp OCR tối đa 6 MB.');
      return;
    }
    setFileName(file.name);
    setStatusMsg('');
    if (
      file.type.startsWith('text/') ||
      file.type.includes('xml') ||
      file.name.toLowerCase().endsWith('.txt') ||
      file.name.toLowerCase().endsWith('.xml')
    ) {
      setRawText(await file.text());
      setImageBase64('');
      setMimeType(file.name.toLowerCase().endsWith('.xml') ? 'application/xml' : 'text/plain');
      return;
    }
    const dataUrl = await new Promise<string>((resolve, reject) => {
      const reader = new FileReader();
      reader.onload = () => resolve(String(reader.result || ''));
      reader.onerror = () => reject(new Error('Không đọc được tệp.'));
      reader.readAsDataURL(file);
    });
    setImageBase64(dataUrl);
    setMimeType(file.type || 'image/png');
  };

  const runOcr = async () => {
    if (!rawText.trim() && !imageBase64) {
      setStatusMsg('Chọn ảnh/PDF hóa đơn hoặc dán nội dung văn bản để trích xuất.');
      return;
    }
    setBusy(true);
    setStatusMsg('');
    try {
      const trimmedText = rawText.trim();
      const isNd123Xml =
        mimeType === 'application/xml' ||
        fileName.toLowerCase().endsWith('.xml') ||
        (trimmedText.startsWith('<') && (trimmedText.includes('<HDon') || trimmedText.includes('<DLHDon')));

      if (isNd123Xml && trimmedText) {
        const xmlRes = await fetch('/api/integrations/einvoice/parse-xml', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          credentials: 'include',
          body: JSON.stringify({ xml: trimmedText, tableId }),
        }).then((r) => r.json());
        if (!xmlRes?.success || !xmlRes?.data) {
          setStatusMsg(xmlRes?.message || 'Không phân tích được XML hóa đơn NĐ 123.');
          return;
        }
        const mappedFields = (xmlRes.data.suggestedFields || {}) as Record<string, unknown>;
        const mappedSub = (xmlRes.data.suggestedSubTables || {}) as Record<string, Array<Record<string, unknown>>>;
        onExtracted({ fields: mappedFields, subTables: mappedSub, engine: 'NĐ 123 XML Parser' });
        const fc = Object.keys(mappedFields).length;
        const sc = Object.values(mappedSub).reduce((acc, rows) => acc + rows.length, 0);
        setStatusMsg(
          `Đã bóc tách HĐ NĐ 123 số ${String(xmlRes.data.invoiceNo || '?')}: điền ${fc} trường${sc > 0 ? ` & ${sc} dòng hàng` : ''}.`
        );
        return;
      }

      const res = await fetch('/api/records/ai-doc-ocr', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({
          tableId,
          rawText: rawText.trim() || undefined,
          imageBase64: imageBase64 || undefined,
          mimeType: mimeType || undefined,
          fileName: fileName || undefined,
        }),
      }).then((r) => r.json());

      if (!res?.success || !res?.data) {
        setStatusMsg(res?.message || 'Không trích xuất được dữ liệu từ chứng từ.');
        return;
      }
      const data = res.data as AiOcrResponse;
      onExtracted(data);
      const count = Object.keys(data.fields || {}).length;
      const subCount = Object.values(data.subTables || {}).reduce((acc, rows) => acc + rows.length, 0);
      setStatusMsg(
        `Đã điền ${count} trường${subCount > 0 ? ` & ${subCount} dòng chi tiết` : ''} (${data.engine || 'OCR'}).` +
          (data.warnings?.length ? ` Lưu ý: ${data.warnings[0]}` : '')
      );
    } catch {
      setStatusMsg('Lỗi kết nối dịch vụ AI OCR.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div style={{ marginBottom: 10 }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
        <button
          type="button"
          className={`btn btn-xs ${openPanel ? 'btn-primary' : 'btn-outline'}`}
          onClick={() => setOpenPanel((p) => !p)}
        >
          <AutoAwesomeRoundedIcon sx={{ fontSize: 14 }} />
          Quét chứng từ (AI OCR)
        </button>
        {statusMsg && (
          <span role="status" style={{ fontSize: 12, color: 'var(--text-secondary)' }}>
            {statusMsg}
          </span>
        )}
      </div>
      {openPanel && (
        <div
          style={{
            marginTop: 8,
            padding: 12,
            borderRadius: 10,
            backgroundColor: 'var(--bg-secondary)',
            border: '1px solid var(--border-subtle)',
            display: 'flex',
            flexDirection: 'column',
            gap: 8,
          }}
        >
          <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
            <label htmlFor={inputId} className="btn btn-outline btn-xs" style={{ cursor: 'pointer' }}>
              <UploadFileRoundedIcon sx={{ fontSize: 14 }} />
              {fileName ? `Tệp: ${fileName}` : 'Chọn ảnh / PDF / XML HĐĐT NĐ123'}
            </label>
            <input
              id={inputId}
              type="file"
              accept="image/*,.pdf,.txt,.xml"
              style={{ display: 'none' }}
              onChange={(e) => {
                const f = e.target.files?.[0];
                e.target.value = '';
                void handleFilePick(f);
              }}
            />
            <button
              type="button"
              className="btn btn-primary btn-xs"
              disabled={busy || (!rawText.trim() && !imageBase64)}
              onClick={() => void runOcr()}
            >
              {busy ? 'Đang trích xuất...' : 'Trích xuất & Điền form'}
            </button>
          </div>
          <label className="form-label" style={{ fontSize: 11.5, margin: 0 }}>
            Hoặc dán văn bản chứng từ / dòng hàng (STT | Tên hàng | SL | Đơn giá | Thành tiền)
          </label>
          <textarea
            className="form-textarea"
            rows={2}
            placeholder="VD: Số hóa đơn: HD-2026-088&#10;Ngày: 18/11/2026&#10;MST: 0312345678&#10;Tổng tiền thanh toán: 12.500.000"
            value={rawText}
            onChange={(e) => setRawText(e.target.value)}
          />
        </div>
      )}
    </div>
  );
}
