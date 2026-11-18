'use client';

import React from 'react';
import CheckCircleRoundedIcon from '@mui/icons-material/CheckCircleRounded';
import ErrorOutlineRoundedIcon from '@mui/icons-material/ErrorOutlineRounded';
import HourglassEmptyRoundedIcon from '@mui/icons-material/HourglassEmptyRounded';
import InfoRoundedIcon from '@mui/icons-material/InfoRounded';

interface StatusBadgeProps {
  status: string;
  label?: string;
  size?: 'sm' | 'md';
}

export default function StatusBadge({ status, label, size = 'md' }: StatusBadgeProps) {
  const norm = (status || '').toUpperCase();
  let bg = '#f1f3f4';
  let color = '#3c4043';
  let border = '#dadce0';
  let icon: React.ReactNode = <InfoRoundedIcon sx={{ fontSize: size === 'sm' ? 12 : 14 }} />;
  let text = label || status;

  if (['ACTIVE', 'VALID', 'SUCCESS', 'COMPLETED', 'DA_DUYET'].includes(norm)) {
    bg = '#e6f4ea';
    color = '#137333';
    border = '#ceead6';
    icon = <CheckCircleRoundedIcon sx={{ fontSize: size === 'sm' ? 12 : 14 }} />;
    if (!label) text = norm === 'ACTIVE' ? 'Hợp lệ' : status;
  } else if (['ERROR', 'INVALID', 'TU_CHOI'].includes(norm)) {
    bg = '#fce8e6';
    color = '#c5221f';
    border = '#fad2cf';
    icon = <ErrorOutlineRoundedIcon sx={{ fontSize: size === 'sm' ? 12 : 14 }} />;
    if (!label) text = norm === 'ERROR' ? 'Lỗi dữ liệu' : status;
  } else if (['PENDING', 'WARNING', 'CHO_DUYET'].includes(norm)) {
    bg = '#fef7e0';
    color = '#b06000';
    border = '#feefc3';
    icon = <HourglassEmptyRoundedIcon sx={{ fontSize: size === 'sm' ? 12 : 14 }} />;
  }

  return (
    <span
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: 5,
        padding: size === 'sm' ? '2px 8px' : '4px 10px',
        borderRadius: 9999,
        fontSize: size === 'sm' ? 11 : 12,
        fontWeight: 600,
        backgroundColor: bg,
        color,
        border: `1px solid ${border}`,
        whiteSpace: 'nowrap',
      }}
    >
      {icon}
      {text}
    </span>
  );
}
