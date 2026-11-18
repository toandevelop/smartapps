'use client';

import React from 'react';
import Dialog from '@mui/material/Dialog';
import DialogTitle from '@mui/material/DialogTitle';
import DialogContent from '@mui/material/DialogContent';
import DialogContentText from '@mui/material/DialogContentText';
import DialogActions from '@mui/material/DialogActions';
import Button from '@mui/material/Button';
import WarningAmberRoundedIcon from '@mui/icons-material/WarningAmberRounded';
import HelpOutlineRoundedIcon from '@mui/icons-material/HelpOutlineRounded';

export interface ConfirmDialogProps {
  open: boolean;
  title?: string;
  message: string;
  confirmText?: string;
  cancelText?: string;
  variant?: 'danger' | 'warning' | 'primary';
  onConfirm: () => void;
  onCancel: () => void;
}

export default function ConfirmDialog({
  open,
  title = 'Xác nhận thao tác',
  message,
  confirmText = 'Xác nhận',
  cancelText = 'Hủy',
  variant = 'danger',
  onConfirm,
  onCancel,
}: ConfirmDialogProps) {
  const isDanger = variant === 'danger';

  return (
    <Dialog open={open} onClose={onCancel} maxWidth="xs" fullWidth>
      <DialogTitle sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
        <div
          style={{
            width: 36,
            height: 36,
            borderRadius: '50%',
            backgroundColor: isDanger ? '#fce8e6' : '#e8f0fe',
            color: isDanger ? '#d93025' : '#1a73e8',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
          }}
        >
          {isDanger ? <WarningAmberRoundedIcon sx={{ fontSize: 20 }} /> : <HelpOutlineRoundedIcon sx={{ fontSize: 20 }} />}
        </div>
        <span style={{ fontSize: 16, fontWeight: 700 }}>{title}</span>
      </DialogTitle>
      <DialogContent>
        <DialogContentText sx={{ fontSize: 13.5, color: '#5f6368' }}>{message}</DialogContentText>
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 2 }}>
        <Button onClick={onCancel} variant="outlined" color="inherit">
          {cancelText}
        </Button>
        <Button onClick={onConfirm} variant="contained" color={isDanger ? 'error' : 'primary'} autoFocus>
          {confirmText}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
