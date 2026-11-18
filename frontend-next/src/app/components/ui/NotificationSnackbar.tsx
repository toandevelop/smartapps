'use client';

import React from 'react';
import Snackbar from '@mui/material/Snackbar';
import Alert, { AlertColor } from '@mui/material/Alert';
import Button from '@mui/material/Button';
import IconButton from '@mui/material/IconButton';
import CloseRoundedIcon from '@mui/icons-material/CloseRounded';

export interface NotificationSnackbarProps {
  open: boolean;
  message: string;
  severity?: AlertColor;
  autoHideDuration?: number;
  actionLabel?: string;
  onAction?: () => void;
  onClose: () => void;
}

export default function NotificationSnackbar({
  open,
  message,
  severity = 'info',
  autoHideDuration = 4000,
  actionLabel,
  onAction,
  onClose,
}: NotificationSnackbarProps) {
  return (
    <Snackbar
      open={open}
      autoHideDuration={actionLabel && onAction ? Math.max(autoHideDuration, 6000) : autoHideDuration}
      onClose={onClose}
      anchorOrigin={{ vertical: 'top', horizontal: 'center' }}
    >
      <Alert
        onClose={onClose}
        severity={severity}
        variant="filled"
        sx={{ borderRadius: '10px', fontWeight: 600, alignItems: 'center' }}
        action={
          actionLabel && onAction ? (
            <div style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
              <Button
                color="inherit"
                size="small"
                onClick={() => {
                  onAction();
                  onClose();
                }}
                sx={{ fontWeight: 700, textTransform: 'none', textDecoration: 'underline', minWidth: 'auto', px: 1 }}
              >
                {actionLabel}
              </Button>
              <IconButton size="small" color="inherit" aria-label="Đóng thông báo" onClick={onClose}>
                <CloseRoundedIcon fontSize="small" />
              </IconButton>
            </div>
          ) : undefined
        }
      >
        {message}
      </Alert>
    </Snackbar>
  );
}
