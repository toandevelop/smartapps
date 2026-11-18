'use client';

import React from 'react';
import Snackbar from '@mui/material/Snackbar';
import Alert, { AlertColor } from '@mui/material/Alert';

export interface NotificationSnackbarProps {
  open: boolean;
  message: string;
  severity?: AlertColor;
  autoHideDuration?: number;
  onClose: () => void;
}

export default function NotificationSnackbar({
  open,
  message,
  severity = 'info',
  autoHideDuration = 4000,
  onClose,
}: NotificationSnackbarProps) {
  return (
    <Snackbar
      open={open}
      autoHideDuration={autoHideDuration}
      onClose={onClose}
      anchorOrigin={{ vertical: 'top', horizontal: 'center' }}
    >
      <Alert onClose={onClose} severity={severity} variant="filled" sx={{ borderRadius: '10px', fontWeight: 600 }}>
        {message}
      </Alert>
    </Snackbar>
  );
}
