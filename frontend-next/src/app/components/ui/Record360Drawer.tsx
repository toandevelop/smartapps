'use client';

import React from 'react';
import Drawer from '@mui/material/Drawer';
import Record360Content, { Record360ContentProps } from './Record360Content';

export interface Record360DrawerProps extends Omit<Record360ContentProps, 'variant' | 'onClose'> {
  onClose: () => void;
}

export default function Record360Drawer({
  open,
  onClose,
  ...rest
}: Record360DrawerProps) {
  return (
    <Drawer
      anchor="right"
      open={open}
      onClose={onClose}
      slotProps={{ paper: { sx: { width: { xs: '100%', sm: 800, lg: 940 }, p: 0 } } }}
    >
      <Record360Content open={open} onClose={onClose} variant="drawer" {...rest} />
    </Drawer>
  );
}
